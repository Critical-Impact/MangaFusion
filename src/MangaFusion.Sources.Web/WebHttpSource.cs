using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.Web.Models;
using MangaFusion.Sources.Web.Util;

namespace MangaFusion.Sources.Web;

/// <summary>Base class for a website-backed source — the C# port of Tachiyomi's <c>HttpSource</c>. A
/// concrete source (or a <c>SourcePlatform</c>) overrides the request/parse pairs (popular / latest /
/// search / details / chapter-list / page-list) in terms of <see cref="WebManga"/>/<see cref="WebChapter"/>/
/// <see cref="WebPage"/>; this base drives the HTTP calls, HTML parsing, and the mapping onto
/// MangaFusion's provider-neutral capability interfaces so no per-source wiring is needed.</summary>
public abstract partial class WebHttpSource(IHttpClientFactory httpClientFactory)
    : IMetadataSource, IChapterSource, IDownloadSource, ISourceCoverHosts
{
    private readonly HtmlParser _parser = new();

    /// <summary>Covers are served from the site's own domain (Madara: <c>/wp-content/uploads/…</c>), so
    /// the proxy is allowed to fetch from the base host and its www/bare variant. Override for a source
    /// whose covers live on a separate CDN.</summary>
    public virtual IReadOnlyList<string> CoverHosts
    {
        get
        {
            var host = new Uri(BaseUrl).Host;
            var alt = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : "www." + host;
            return [host, alt];
        }
    }

    // ---- Source identity (override in concrete sources) --------------------------------------

    /// <summary>Display name of the site, e.g. "MangaCrazy".</summary>
    public abstract string Name { get; }

    /// <summary>Base URL without a trailing slash, e.g. "https://mangacrazy.net".</summary>
    public abstract string BaseUrl { get; }

    /// <summary>ISO 639-1 language code the site serves (or "all"/"multi").</summary>
    public abstract string Lang { get; }

    /// <summary>Bumped when a site changes incompatibly, to mint a fresh <see cref="Id"/>.</summary>
    protected virtual int VersionId => 1;

    /// <summary>Stable, route-safe source id derived from the name (override only to pin a legacy id).</summary>
    public virtual string Id => $"web.{Slugify(Name)}";

    public string DisplayName => Name;

    public virtual SourceCapabilities Capabilities =>
        SourceCapabilities.Metadata | SourceCapabilities.Chapters | SourceCapabilities.Download;

    public virtual IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

    /// <summary>Content rating applied to every series from this source (NSFW sites override).</summary>
    protected virtual ContentRating DefaultContentRating => ContentRating.Unknown;

    /// <summary>Filters this source advertises for browsing. Empty by default; surfaced in the UI later.</summary>
    protected virtual FilterList GetFilterList() => new();

    // ---- Request/parse pairs (override these) ------------------------------------------------

    protected virtual HttpRequestMessage PopularRequest(int page) => throw new NotSupportedException();
    protected virtual MangasPage PopularParse(IDocument document) => throw new NotSupportedException();

    protected virtual HttpRequestMessage LatestRequest(int page) => throw new NotSupportedException();
    protected virtual MangasPage LatestParse(IDocument document) => throw new NotSupportedException();

    protected virtual HttpRequestMessage SearchRequest(int page, string query, FilterList filters) =>
        throw new NotSupportedException();
    protected virtual MangasPage SearchParse(IDocument document) => throw new NotSupportedException();

    protected virtual HttpRequestMessage MangaDetailsRequest(WebManga manga) => Get(Absolute(manga.Url)!);
    protected virtual WebManga MangaDetailsParse(IDocument document) => throw new NotSupportedException();

    protected virtual HttpRequestMessage ChapterListRequest(WebManga manga) => Get(Absolute(manga.Url)!);
    protected virtual IReadOnlyList<WebChapter> ChapterListParse(IDocument document) =>
        throw new NotSupportedException();

    protected virtual HttpRequestMessage PageListRequest(WebChapter chapter) => Get(Absolute(chapter.Url)!);
    protected virtual IReadOnlyList<WebPage> PageListParse(IDocument document) =>
        throw new NotSupportedException();

    /// <summary>Resolves a page's image URL when the page list didn't embed it. Default: the page URL.</summary>
    protected virtual Task<string> GetImageUrlAsync(WebPage page, CancellationToken ct) =>
        Task.FromResult(page.ImageUrl ?? page.Url);

    // ---- Orchestration (override rarely) -----------------------------------------------------

    protected virtual async Task<MangasPage> GetPopularAsync(int page, CancellationToken ct) =>
        PopularParse(await FetchAsync(PopularRequest(page), ct));

    protected virtual async Task<MangasPage> GetLatestAsync(int page, CancellationToken ct) =>
        LatestParse(await FetchAsync(LatestRequest(page), ct));

    protected virtual async Task<MangasPage> GetSearchAsync(
        int page, string query, FilterList filters, CancellationToken ct) =>
        SearchParse(await FetchAsync(SearchRequest(page, query, filters), ct));

    protected virtual async Task<WebManga> GetMangaDetailsAsync(WebManga manga, CancellationToken ct)
    {
        var parsed = MangaDetailsParse(await FetchAsync(MangaDetailsRequest(manga), ct));
        parsed.Url = manga.Url; // preserve identity even if the page doesn't echo it
        return parsed;
    }

    protected virtual async Task<IReadOnlyList<WebChapter>> GetChapterListAsync(
        WebManga manga, CancellationToken ct) =>
        ChapterListParse(await FetchAsync(ChapterListRequest(manga), ct));

    protected virtual async Task<IReadOnlyList<WebPage>> GetPageListAsync(
        WebChapter chapter, CancellationToken ct) =>
        PageListParse(await FetchAsync(PageListRequest(chapter), ct));

    // ---- Capability interface implementations (mapping to MangaFusion DTOs) ------------------

    public async Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var page = (query.Limit > 0 ? query.Offset / query.Limit : 0) + 1;

        MangasPage result;
        if (!string.IsNullOrWhiteSpace(query.Text))
            result = await GetSearchAsync(page, query.Text!, GetFilterList(), ct);
        else if (query.Order is SearchOrder.Newest or SearchOrder.LatestUploadedChapter)
            result = await GetLatestAsync(page, ct);
        else
            result = await GetPopularAsync(page, ct);

        var items = result.Mangas.Select(ToSeries).ToList();
        // Web sites don't report totals; synthesise one that keeps a "next page" available when there is one.
        var total = query.Offset + items.Count + (result.HasNextPage ? Math.Max(query.Limit, 1) : 0);
        return new PagedResult<SourceSeries>(items, total, query.Limit, query.Offset);
    }

    public async Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default)
    {
        var manga = new WebManga { Url = UrlUtil.DecodeId(sourceSeriesId) };
        var full = await GetMangaDetailsAsync(manga, ct);
        return ToSeries(full);
    }

    public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceTag>>(
            GetFilterList()
                .OfType<GroupFilter>()
                .SelectMany(g => g.State.OfType<Filter>().Select(f => new SourceTag(f.Name, f.Name, g.Name)))
                .ToList());

    public async Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceSeriesId, ChapterQuery query, CancellationToken ct = default)
    {
        // Single-language site: if the caller asked for other languages only, we have nothing to offer.
        if (query.TranslatedLanguages.Count > 0 &&
            !query.TranslatedLanguages.Contains(Lang, StringComparer.OrdinalIgnoreCase))
            return new PagedResult<SourceChapter>([], 0, query.Limit, query.Offset);

        var manga = new WebManga { Url = UrlUtil.DecodeId(sourceSeriesId) };
        var chapters = await GetChapterListAsync(manga, ct);

        var ordered = query.Order == ChapterOrder.ChapterDescending
            ? chapters.OrderByDescending(c => c.ChapterNumber)
            : chapters.OrderBy(c => c.ChapterNumber);

        var mapped = ordered.Select(ToChapter).ToList();
        // No incremental `CreatedSince` on scraped sites — return the full list; the caller de-dupes.
        var slice = mapped.Skip(query.Offset).Take(query.Limit).ToList();
        return new PagedResult<SourceChapter>(slice, mapped.Count, query.Limit, query.Offset);
    }

    public async Task<SourcePageSet> GetPagesAsync(
        string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default)
    {
        var chapter = new WebChapter { Url = UrlUtil.DecodeId(sourceChapterId) };
        var pages = await GetPageListAsync(chapter, ct);

        var sourcePages = new List<SourcePage>(pages.Count);
        foreach (var page in pages.OrderBy(p => p.Index))
        {
            var imageUrl = string.IsNullOrEmpty(page.ImageUrl)
                ? await GetImageUrlAsync(page, ct)
                : page.ImageUrl;
            var abs = Absolute(imageUrl)!;
            sourcePages.Add(new SourcePage(page.Index, abs, FileName(page.Index, abs)));
        }

        return new SourcePageSet
        {
            SourceChapterId = sourceChapterId,
            Pages = sourcePages,
            Quality = quality, // scraped sources have a single quality; flag is ignored
            // Image CDNs behind scraper sites commonly gate on a matching Referer and a browser
            // User-Agent; the downloader applies these when fetching each page image.
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Referer"] = BaseUrl + "/",
                ["User-Agent"] = WebSourceConstants.UserAgent,
            },
        };
    }

    // ---- Mapping helpers ---------------------------------------------------------------------

    private SourceSeries ToSeries(WebManga manga) => new()
    {
        SourceId = Id,
        SourceSeriesId = UrlUtil.EncodeId(manga.Url),
        Title = manga.Title,
        Description = manga.Description,
        CoverUrl = Absolute(manga.ThumbnailUrl),
        Authors = manga.Author is { Length: > 0 } a ? [a] : [],
        Artists = manga.Artist is { Length: > 0 } ar ? [ar] : [],
        Tags = manga.GetGenres(),
        ContentRating = DefaultContentRating,
        Status = ToStatus(manga.Status),
        OriginalLanguage = Lang,
        AvailableTranslatedLanguages = [Lang],
        SiteUrl = Absolute(manga.Url),
    };

    private SourceChapter ToChapter(WebChapter chapter) => new()
    {
        SourceId = Id,
        SourceChapterId = UrlUtil.EncodeId(chapter.Url),
        Number = chapter.ChapterNumber >= 0 ? FormatNumber(chapter.ChapterNumber) : null,
        Title = string.IsNullOrWhiteSpace(chapter.Name) ? null : chapter.Name,
        Language = Lang,
        ScanlationGroups = string.IsNullOrWhiteSpace(chapter.Scanlator) ? [] : [chapter.Scanlator!],
        PublishedAt = chapter.DateUpload,
        IsExternal = false,
    };

    private static PublicationStatus ToStatus(WebMangaStatus status) => status switch
    {
        WebMangaStatus.Ongoing => PublicationStatus.Ongoing,
        WebMangaStatus.Completed or WebMangaStatus.PublishingFinished => PublicationStatus.Completed,
        WebMangaStatus.OnHiatus => PublicationStatus.Hiatus,
        WebMangaStatus.Cancelled => PublicationStatus.Cancelled,
        _ => PublicationStatus.Unknown,
    };

    private static string FormatNumber(float number) =>
        number % 1 == 0
            ? ((long)number).ToString(CultureInfo.InvariantCulture)
            : number.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FileName(int index, string url)
    {
        var ext = Path.GetExtension(new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).AbsolutePath
            : url);
        if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";
        return $"{index + 1:D4}{ext}";
    }

    // ---- HTTP + parsing plumbing -------------------------------------------------------------

    /// <summary>Builds a GET request. Override <see cref="HeadersBuilder"/> for custom headers.</summary>
    protected HttpRequestMessage Get(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        HeadersBuilder(request);
        return request;
    }

    /// <summary>Builds a POST request with a URL-encoded form body and XHR headers (for AJAX endpoints).</summary>
    protected HttpRequestMessage PostForm(string url, IReadOnlyDictionary<string, string>? form = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new FormUrlEncodedContent(form ?? new Dictionary<string, string>()),
        };
        HeadersBuilder(request);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "XMLHttpRequest");
        return request;
    }

    /// <summary>Adds per-request headers. Default adds a Referer of the site root; override to extend.</summary>
    protected virtual void HeadersBuilder(HttpRequestMessage request) =>
        request.Headers.TryAddWithoutValidation("Referer", BaseUrl + "/");

    /// <summary>Resolves a possibly-relative site path against <see cref="BaseUrl"/>.</summary>
    protected string? Absolute(string? path) => UrlUtil.Absolute(BaseUrl, path);

    /// <summary>Parses an HTML string into a document.</summary>
    protected IDocument ParseHtml(string html) => _parser.ParseDocument(html);

    /// <summary>Sends a request on the shared web-source client and parses the HTML response body.</summary>
    protected async Task<IDocument> FetchAsync(HttpRequestMessage request, CancellationToken ct) =>
        (await SendForDocumentAsync(request, tolerateError: false, ct))!;

    /// <summary>Sends a request and parses the HTML body. When <paramref name="tolerateError"/> is true,
    /// a non-success status yields null instead of throwing (used to probe fallback endpoints).</summary>
    protected async Task<IDocument?> SendForDocumentAsync(
        HttpRequestMessage request, bool tolerateError, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(WebSourceConstants.HttpClient);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (tolerateError) return null;
            response.EnsureSuccessStatusCode();
        }
        var html = await response.Content.ReadAsStringAsync(ct);
        return _parser.ParseDocument(html);
    }

    private static string Slugify(string name)
    {
        var slug = NonSlugChars().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "source" : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugChars();
}
