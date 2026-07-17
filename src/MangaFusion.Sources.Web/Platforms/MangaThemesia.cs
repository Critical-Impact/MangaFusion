using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using MangaFusion.Sources.Web.Models;
using MangaFusion.Sources.Web.Parsing;
using MangaFusion.Sources.Web.Util;

namespace MangaFusion.Sources.Web.Platforms;

/// <summary>The <b>MangaThemesia</b> (a.k.a. WPMangaStream) WordPress theme — the second most common
/// manga-site engine after Madara. A pragmatic C# port of Tachiyomi's <c>MangaThemesia</c> multisrc:
/// popular/latest/search share one <c>/manga/?title=…&amp;order=…</c> endpoint; details read the
/// <c>.tsinfo .imptdt</c>/<c>.infotable</c> label rows (Jsoup <c>:contains</c> reworked in C#); pages
/// come from <c>#readerarea img</c> or, when the site injects them, the <c>ts_reader</c> script's
/// <c>"images":[…]</c> array. Omitted for the PoC: server-side genre filters and view-count pings.</summary>
public abstract class MangaThemesia(IHttpClientFactory httpClientFactory) : ParsedWebHttpSource(httpClientFactory)
{
    protected virtual string MangaUrlDirectory => "/manga";
    protected virtual string[] DateFormats => ["MMMM d, yyyy", "MMMM dd, yyyy", "MMM d, yyyy", "yyyy-MM-dd"];

    private static readonly Regex ImageListRegex =
        new("\"images\"\\s*:\\s*(\\[.*?\\])", RegexOptions.Singleline | RegexOptions.Compiled);

    // ---- Listing (popular/latest/search all hit the same search endpoint) --------------------
    protected override HttpRequestMessage PopularRequest(int page) => Get(SearchUrl(page, "", "popular"));
    protected override HttpRequestMessage LatestRequest(int page) => Get(SearchUrl(page, "", "update"));
    protected override HttpRequestMessage SearchRequest(int page, string query, FilterList filters) =>
        Get(SearchUrl(page, query, ""));

    private string SearchUrl(int page, string query, string order)
    {
        var url = $"{BaseUrl}{MangaUrlDirectory}/?title={Uri.EscapeDataString(query)}&page={page}";
        return order.Length > 0 ? $"{url}&order={order}" : url;
    }

    private const string ListSelector = ".utao .uta .imgu, .listupd .bs .bsx, .listo .bs .bsx";
    private const string ListNextSelector = "div.pagination .next, div.hpage .r";

    protected override string PopularMangaSelector() => ListSelector;
    protected override string LatestUpdatesSelector() => ListSelector;
    protected override string SearchMangaSelector() => ListSelector;
    protected override string? PopularMangaNextPageSelector() => ListNextSelector;
    protected override string? LatestUpdatesNextPageSelector() => ListNextSelector;
    protected override string? SearchMangaNextPageSelector() => ListNextSelector;

    protected override WebManga PopularMangaFromElement(IElement element) => ListItem(element);
    protected override WebManga LatestUpdatesFromElement(IElement element) => ListItem(element);
    protected override WebManga SearchMangaFromElement(IElement element) => ListItem(element);

    private static WebManga ListItem(IElement element)
    {
        var link = element.SelectFirst("a");
        var title = link.Attr("title");
        return new WebManga
        {
            Url = UrlUtil.RemoveDomain(link.Attr("href")),
            Title = title.Length > 0 ? title : link.Text(),
            ThumbnailUrl = ImgAttr(element.SelectFirst("img")),
        };
    }

    // ---- Details -----------------------------------------------------------------------------
    protected virtual string SeriesDetailsSelector => "div.bigcontent, div.animefull, div.main-info, div.postbody";
    protected virtual string SeriesTitleSelector => "h1.entry-title, .ts-breadcrumb li:last-child span";
    protected virtual string SeriesDescriptionSelector => ".desc, .entry-content[itemprop=description]";
    protected virtual string SeriesGenreSelector => "div.gnr a, .mgen a, .seriestugenre a";
    protected virtual string SeriesThumbnailSelector => ".infomanga > div[itemprop=image] img, .thumb img";

    protected override WebManga MangaDetailsParse(IDocument document)
    {
        var details = document.SelectFirst(SeriesDetailsSelector) ?? document.DocumentElement;

        var manga = new WebManga
        {
            Url = "", // preserved by the caller
            Title = details.SelectFirst(SeriesTitleSelector).Text(),
            Author = Clean(InfoValue(details, "Author")),
            Artist = Clean(InfoValue(details, "Artist")),
        };

        var description = string.Join("\n", details.Select(SeriesDescriptionSelector).Select(e => e.Text()))
            .Trim();
        if (description.Length > 0) manga.Description = description;

        manga.ThumbnailUrl = ImgAttr(details.SelectFirst(SeriesThumbnailSelector));
        manga.Status = ParseStatus(InfoValue(details, "Status"));

        var genres = details.Select(SeriesGenreSelector).Select(e => e.Text()).Where(g => g.Length > 0).ToList();
        var type = Clean(InfoValue(details, "Type"));
        if (type is not null) genres.Add(type);
        manga.Genre = string.Join(", ", genres.DistinctBy(g => g.ToLowerInvariant()));

        return manga;
    }

    /// <summary>Reads a labelled info value (Author/Artist/Status/Type) from the two common layouts —
    /// <c>.tsinfo .imptdt</c> label boxes and <c>.infotable</c> rows — standing in for the original's
    /// <c>:contains(label)</c> selectors.</summary>
    private static string? InfoValue(IElement details, string label)
    {
        foreach (var box in details.SelectContaining(".tsinfo .imptdt", label))
        {
            var value = (box.SelectFirst("i") ?? box.SelectFirst("a")).Text();
            if (value.Length > 0) return value;
        }
        foreach (var row in details.SelectContaining(".infotable tr", label))
        {
            var value = row.Select("td").LastOrDefault().Text();
            if (value.Length > 0) return value;
        }
        return null;
    }

    // ---- Chapters ----------------------------------------------------------------------------
    protected override string ChapterListSelector() => "#chapterlist li, div.bxcl li, div.cl li";

    protected override WebChapter ChapterFromElement(IElement element)
    {
        var link = element.SelectFirst("a");
        var name = element.SelectFirst(".lch a, .chapternum").Text();
        if (name.Length == 0) name = link.Text();
        return new WebChapter
        {
            Url = UrlUtil.RemoveDomain(link.Attr("href")),
            Name = name,
            ChapterNumber = ChapterRecognition.Parse(name),
            DateUpload = ParseDate(element.SelectFirst(".chapterdate").Text()),
        };
    }

    // ---- Pages -------------------------------------------------------------------------------
    protected virtual string PageSelector => "div#readerarea img";

    protected override IReadOnlyList<WebPage> PageListParse(IDocument document)
    {
        var htmlPages = document.Select(PageSelector)
            .Select(ImgAttr)
            .Where(u => !string.IsNullOrEmpty(u))
            .Select((u, i) => new WebPage(i, imageUrl: u))
            .ToList();
        if (htmlPages.Count > 0) return htmlPages;

        // Many MangaThemesia sites inject pages via a `ts_reader.run({ ... "images":[ ... ] })` script.
        var scripts = string.Concat(document.Select("script").Select(s => s.TextContent));
        var match = ImageListRegex.Match(scripts);
        if (match.Success)
        {
            try
            {
                var urls = JsonSerializer.Deserialize<List<string>>(match.Groups[1].Value) ?? [];
                return urls.Where(u => !string.IsNullOrEmpty(u))
                    .Select((u, i) => new WebPage(i, imageUrl: u))
                    .ToList();
            }
            catch (JsonException)
            {
                // fall through to empty
            }
        }
        return [];
    }

    // ---- Helpers -----------------------------------------------------------------------------
    private static string? ImgAttr(IElement? element)
    {
        if (element is null) return null;
        foreach (var attr in (string[])["data-lazy-src", "data-src", "data-cfsrc"])
        {
            var value = element.Attr(attr).Trim();
            if (value.Length > 0) return value;
        }
        var src = element.Attr("src").Trim();
        return src.Length > 0 ? src : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value is "-" or "N/A" or "n/a" or "Unknown" ? null : value.Trim();

    private DateTimeOffset? ParseDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        if (DateTime.TryParseExact(date.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return new DateTimeOffset(d, TimeSpan.Zero);
        if (DateTime.TryParse(date.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose))
            return new DateTimeOffset(loose, TimeSpan.Zero);
        return null;
    }

    private static WebMangaStatus ParseStatus(string? status)
    {
        if (status is null) return WebMangaStatus.Unknown;
        var s = status.ToLowerInvariant();
        if (Contains(s, "ongoing", "on going", "en cours", "publishing", "updating", "en curso", "berjalan", "连载中", "devam ediyor", "em lançamento", "em andamento"))
            return WebMangaStatus.Ongoing;
        if (Contains(s, "completed", "complete", "completo", "terminé", "finished", "finalizado", "tamat", "完結", "已完结", "concluído", "concluido"))
            return WebMangaStatus.Completed;
        if (Contains(s, "canceled", "cancelled", "cancelado", "dropped", "discontinued", "abandonné"))
            return WebMangaStatus.Cancelled;
        if (Contains(s, "hiatus", "on hold", "pausado", "en espera", "en pause", "hiato"))
            return WebMangaStatus.OnHiatus;
        return WebMangaStatus.Unknown;
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.OrdinalIgnoreCase));
}
