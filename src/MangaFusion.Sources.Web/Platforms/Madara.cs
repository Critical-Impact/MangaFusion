using System.Globalization;
using AngleSharp.Dom;
using MangaFusion.Sources.Web.Models;
using MangaFusion.Sources.Web.Parsing;
using MangaFusion.Sources.Web.Util;

namespace MangaFusion.Sources.Web.Platforms;

/// <summary>The <b>Madara</b> WordPress theme — the single most common manga-site engine (hundreds of
/// sites). A pragmatic C# port of Tachiyomi's <c>Madara</c> multisrc base: a concrete site is a small
/// subclass supplying <c>Name</c>/<c>BaseUrl</c>/<c>Lang</c> plus the odd override.
///
/// Ported: the GET-based popular/latest/search, standard details parsing (incl. the Jsoup
/// <c>:contains</c> status/type fields, reworked in C#), the AJAX chapter-list fallback (old
/// admin-ajax and the new <c>/ajax/chapters</c> endpoint), and page-list parsing. Deliberately omitted
/// for the PoC: <c>madara_load_more</c> pagination, server-side genre filters, and AES-protected
/// (<c>chapter-protector</c>) page decryption.</summary>
public abstract class Madara(IHttpClientFactory httpClientFactory) : ParsedWebHttpSource(httpClientFactory)
{
    // ---- Overridable configuration -----------------------------------------------------------
    protected virtual string MangaSubString => "manga";
    protected virtual bool UseNewChapterEndpoint => false;
    protected virtual string ChapterUrlSuffix => "?style=list";
    // InvariantCulture (not "en-US") because the app runs in globalization-invariant mode; the
    // invariant culture still uses English month names, which is what these en sites emit.
    protected virtual CultureInfo DateLocale => CultureInfo.InvariantCulture;
    protected virtual string[] DateFormats => ["MMMM d, yyyy", "MMMM dd, yyyy", "MMM d, yyyy", "MMM dd, yyyy"];

    // ---- Popular / Latest --------------------------------------------------------------------
    protected virtual string PopularMangaUrlSelector => "div.post-title a";

    protected override HttpRequestMessage PopularRequest(int page) =>
        Get($"{BaseUrl}/{MangaSubString}/{SearchPage(page)}?m_orderby=views");

    protected override HttpRequestMessage LatestRequest(int page) =>
        Get($"{BaseUrl}/{MangaSubString}/{SearchPage(page)}?m_orderby=latest");

    protected override string PopularMangaSelector() => "div.page-item-detail, .manga__item";
    protected override WebManga PopularMangaFromElement(IElement element) =>
        MangaFromElement(element, PopularMangaUrlSelector);
    protected override string? PopularMangaNextPageSelector() =>
        "div.nav-previous, nav.navigation-ajax, a.nextpostslink";

    protected override string LatestUpdatesSelector() => PopularMangaSelector();
    protected override WebManga LatestUpdatesFromElement(IElement element) => PopularMangaFromElement(element);
    protected override string? LatestUpdatesNextPageSelector() => PopularMangaNextPageSelector();

    // ---- Search ------------------------------------------------------------------------------
    protected virtual string SearchMangaUrlSelector => "div.post-title a";

    protected override HttpRequestMessage SearchRequest(int page, string query, FilterList filters) =>
        Get($"{BaseUrl}/{SearchPage(page)}?s={Uri.EscapeDataString(query)}&post_type=wp-manga");

    protected override string SearchMangaSelector() => "div.c-tabs-item__content, .manga__item";
    protected override WebManga SearchMangaFromElement(IElement element) =>
        MangaFromElement(element, SearchMangaUrlSelector);
    protected override string? SearchMangaNextPageSelector() => PopularMangaNextPageSelector();

    protected virtual string SearchPage(int page) => page == 1 ? "" : $"page/{page}/";

    private WebManga MangaFromElement(IElement element, string urlSelector)
    {
        var link = element.SelectFirst(urlSelector);
        var manga = new WebManga
        {
            Url = UrlUtil.RemoveDomain(link.Attr("href")),
            Title = link.OwnText() is { Length: > 0 } t ? t : link.Text(),
        };
        var img = element.SelectFirst("img");
        if (img is not null) manga.ThumbnailUrl = ImageFromElement(img);
        return manga;
    }

    // ---- Details -----------------------------------------------------------------------------
    protected virtual string DetailsTitleSelector => "div.post-title h3, div.post-title h1, #manga-title > h1";
    protected virtual string DetailsAuthorSelector => "div.author-content > a, div.manga-authors > a";
    protected virtual string DetailsArtistSelector => "div.artist-content > a";
    protected virtual string DetailsDescriptionSelector =>
        "div.description-summary div.summary__content, div.summary_content div.post-content_item > h5 + div, div.summary_content div.manga-excerpt";
    protected virtual string DetailsThumbnailSelector => "div.summary_image img";
    protected virtual string DetailsGenreSelector => "div.genres-content a";

    protected override WebManga MangaDetailsParse(IDocument document)
    {
        var titleEl = document.SelectFirst(DetailsTitleSelector);
        var manga = new WebManga
        {
            Url = "", // filled by the caller (GetMangaDetailsAsync preserves identity)
            Title = titleEl.OwnText() is { Length: > 0 } t ? t : titleEl.Text(),
        };

        var authors = document.Select(DetailsAuthorSelector).Select(e => e.Text()).Where(NotUpdating).ToList();
        if (authors.Count > 0) manga.Author = string.Join(", ", authors);

        var artists = document.Select(DetailsArtistSelector).Select(e => e.Text()).Where(NotUpdating).ToList();
        if (artists.Count > 0) manga.Artist = string.Join(", ", artists);

        var descEl = document.SelectFirst(DetailsDescriptionSelector);
        if (descEl is not null)
        {
            var paragraphs = descEl.Select("p").Select(p => p.Text()).Where(s => s.Length > 0).ToList();
            manga.Description = paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : descEl.Text();
        }

        var thumb = document.SelectFirst(DetailsThumbnailSelector);
        if (thumb is not null) manga.ThumbnailUrl = ImageFromElement(thumb);

        manga.Status = ParseStatus(document);

        var genres = document.Select(DetailsGenreSelector).Select(e => e.Text()).Where(s => s.Length > 0).ToList();
        // "Type" (manga/manhwa/manhua) lives behind a Jsoup :contains selector in the original.
        var seriesType = document.SelectContaining(".post-content_item", "Type")
            .FirstOrDefault()?.SelectFirst(".summary-content").OwnText();
        if (!string.IsNullOrWhiteSpace(seriesType) && NotUpdating(seriesType) && seriesType != "-")
            genres.Add(seriesType);
        manga.Genre = string.Join(", ", genres.DistinctBy(g => g.ToLowerInvariant()));

        return manga;
    }

    private WebMangaStatus ParseStatus(IDocument document)
    {
        // Original selector: "div.summary-content, div.summary-heading:contains(Status) + div".
        // Prefer the value next to a "Status" heading (unambiguous); fall back to the last
        // .summary-content (matches the original when there's no labelled heading).
        var statusEl = document.SelectContaining("div.summary-heading", "Status")
                           .Select(h => h.NextElementSibling)
                           .LastOrDefault(sibling => sibling is not null)
                       ?? document.Select("div.summary-content").LastOrDefault();

        var text = statusEl.Text();
        text = new string(text.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray()).Trim();

        if (Matches(text, CompletedStatuses)) return WebMangaStatus.Completed;
        if (Matches(text, OngoingStatuses)) return WebMangaStatus.Ongoing;
        if (Matches(text, HiatusStatuses)) return WebMangaStatus.OnHiatus;
        if (Matches(text, CanceledStatuses)) return WebMangaStatus.Cancelled;
        return WebMangaStatus.Unknown;
    }

    // ---- Chapters ----------------------------------------------------------------------------
    protected virtual string ChapterUrlElementSelector => "a";
    protected virtual string ChapterDateSelector => "span.chapter-release-date";

    protected override string ChapterListSelector() => "li.wp-manga-chapter";

    protected override WebChapter ChapterFromElement(IElement element)
    {
        var link = element.SelectFirst(ChapterUrlElementSelector);
        var url = UrlUtil.RemoveDomain(link.Attr("href"));
        var paged = url.IndexOf("?style=paged", StringComparison.Ordinal);
        if (paged >= 0) url = url[..paged];
        if (!url.EndsWith(ChapterUrlSuffix, StringComparison.Ordinal)) url += ChapterUrlSuffix;

        var name = link.Text();
        return new WebChapter
        {
            Url = url,
            Name = name,
            ChapterNumber = ChapterRecognition.Parse(name),
            DateUpload = ParseChapterDate(element.SelectFirst(ChapterDateSelector).Text()),
        };
    }

    // Madara often ships an empty chapter list on the manga page and loads it over AJAX.
    protected override async Task<IReadOnlyList<WebChapter>> GetChapterListAsync(
        WebManga manga, CancellationToken ct)
    {
        var document = await FetchAsync(ChapterListRequest(manga), ct);
        var chapters = ChapterListParse(document);
        if (chapters.Count > 0) return chapters;

        var holder = document.SelectFirst("div[id^=manga-chapters-holder]");
        if (holder is null) return chapters;

        var mangaUrl = Absolute(manga.Url)!.TrimEnd('/');
        IDocument? ajax = null;

        if (UseNewChapterEndpoint)
        {
            ajax = await SendForDocumentAsync(PostForm($"{mangaUrl}/ajax/chapters"), tolerateError: true, ct);
        }
        else
        {
            var form = new Dictionary<string, string> { ["action"] = "manga_get_chapters", ["manga"] = holder.Attr("data-id") };
            ajax = await SendForDocumentAsync(PostForm($"{BaseUrl}/wp-admin/admin-ajax.php", form), tolerateError: true, ct)
                // Newer Madara 400s the old endpoint — fall back to the new one.
                ?? await SendForDocumentAsync(PostForm($"{mangaUrl}/ajax/chapters"), tolerateError: true, ct);
        }

        return ajax is null ? chapters : ChapterListParse(ajax);
    }

    // ---- Pages -------------------------------------------------------------------------------
    // Simplified from the original (which used Jsoup :has/:not) to plain image selectors.
    protected virtual string PageListSelector =>
        "div.page-break img, li.blocks-gallery-item img, .reading-content .text-left img, .reading-content img";

    protected override IReadOnlyList<WebPage> PageListParse(IDocument document) =>
        document.Select(PageListSelector)
            .Select((img, i) => new WebPage(i, imageUrl: ImageFromElement(img)))
            .Where(p => !string.IsNullOrEmpty(p.ImageUrl))
            .Select((p, i) => new WebPage(i, imageUrl: p.ImageUrl)) // re-index after filtering
            .ToList();

    // ---- Helpers -----------------------------------------------------------------------------

    /// <summary>Reads the best image URL from an element's lazy-load attributes (data-src/srcset/…),
    /// preferring the first attribute that carries a non-blank value.</summary>
    protected virtual string? ImageFromElement(IElement element)
    {
        if (NotBlank(element, "data-src", out var dataSrc)) return dataSrc;
        if (NotBlank(element, "data-lazy-src", out var lazy)) return lazy;
        if (NotBlank(element, "srcset", out var srcset)) return BestFromSrcSet(srcset);
        if (NotBlank(element, "data-cfsrc", out var cf)) return cf;
        if (NotBlank(element, "data-manga-src", out var mangaSrc)) return mangaSrc;
        return element.Attr("src").Trim();

        static bool NotBlank(IElement el, string attr, out string value)
        {
            value = el.Attr(attr).Trim();
            return value.Length > 0;
        }
    }

    private static string? BestFromSrcSet(string srcset)
    {
        // "url1 320w, url2 640w" → the last (usually largest) URL.
        var urls = srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(u => !string.IsNullOrEmpty(u))
            .ToList();
        return urls.Count > 0 ? urls[^1] : null;
    }

    private DateTimeOffset? ParseChapterDate(string date)
    {
        if (string.IsNullOrWhiteSpace(date)) return null;
        if (DateTime.TryParseExact(date.Trim(), DateFormats, DateLocale, DateTimeStyles.None, out var exact))
            return new DateTimeOffset(exact, TimeSpan.Zero);
        if (DateTime.TryParse(date.Trim(), DateLocale, DateTimeStyles.None, out var loose))
            return new DateTimeOffset(loose, TimeSpan.Zero);
        return null; // relative dates ("2 days ago") are not parsed in the PoC
    }

    private static bool NotUpdating(string text) =>
        !text.Contains("Updating", StringComparison.OrdinalIgnoreCase) &&
        !text.Contains("Atualizando", StringComparison.OrdinalIgnoreCase);

    private static bool Matches(string text, string[] list) =>
        list.Any(s => string.Equals(s, text, StringComparison.OrdinalIgnoreCase));

    private static readonly string[] CompletedStatuses =
        ["Completed", "Completo", "Completado", "Concluído", "Concluido", "Finalizado", "Terminé", "Tamamlandı"];
    private static readonly string[] OngoingStatuses =
        ["OnGoing", "Ongoing", "Updating", "Em Lançamento", "Em andamento", "En cours", "En Curso", "En curso", "Devam Ediyor"];
    private static readonly string[] HiatusStatuses =
        ["On Hold", "Pausado", "En espera", "Durduruldu", "En Pause"];
    private static readonly string[] CanceledStatuses =
        ["Canceled", "Cancelled", "Cancelado", "İptal Edildi", "Annulé"];
}
