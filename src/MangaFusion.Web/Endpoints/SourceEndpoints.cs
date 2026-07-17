using System.Collections.Concurrent;
using System.Diagnostics;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Web.Models;
using Microsoft.Extensions.Caching.Memory;

namespace MangaFusion.Web.Endpoints;

/// <summary>Source browsing + admin credential management endpoints. Kept out of Program.cs.</summary>
public static class SourceEndpoints
{
    public const string CoverProxyClient = "cover-proxy";

    private static readonly HashSet<string> AllowedCoverHosts =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "uploads.mangadex.org",
            "cdn.mangaupdates.com",
            "comicvine.gamespot.com",
            "static.comicvine.com",
            // Jetpack Photon — the shared WordPress image CDN many Madara/MangaThemesia sites serve
            // their covers through (e.g. https://i1.wp.com/site.com/wp-content/...). Safe to relay: it's
            // a public image proxy on Automattic's infrastructure, not a gateway into ours.
            "i0.wp.com",
            "i1.wp.com",
            "i2.wp.com",
            "i3.wp.com",
        };

    /// <summary>Hosts already logged as rejected, so the cover proxy warns about each unlisted host only
    /// once per process rather than on every blocked thumbnail.</summary>
    private static readonly ConcurrentDictionary<string, byte> LoggedRejectedCoverHosts = new();

    public static void MapSourceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sources").RequireAuthorization();

        group.MapGet("", ListSources);
        group.MapGet("/{id}/credentials/fields", GetCredentialFields).RequireAuthorization("Admin");
        group.MapPut("/{id}/credentials", SetCredentials).RequireAuthorization("Admin");
        group.MapPost("/{id}/credentials/test", TestCredentials).RequireAuthorization("Admin");
        group.MapGet("/{id}/tags", GetTags);
        group.MapGet("/{id}/search", Search);
        group.MapGet("/{id}/series/{seriesId}", GetSeries);
        group.MapGet("/{id}/series/{seriesId}/chapters", GetChapters);
        group.MapGet("/{id}/chapters/{chapterId}/manifest", GetChapterManifest);
        group.MapGet("/{id}/chapters/{chapterId}/pages/{index:int}", ProxyPage);
        group.MapGet("/{id}/cover", ProxyCover);
    }

    private static async Task<IResult> ListSources(
        ISourceRegistry registry, ISourceCredentialStore credentials, string? kind, CancellationToken ct)
    {
        // No kind = every source (the admin credentials screen wants the full list); with a kind, only the
        // sources that serve that library — this is what backs the browse/search source pickers.
        var sources = kind is null ? registry.All : registry.ForKind(MediaKindQuery.Parse(kind));

        var summaries = new List<SourceSummaryDto>();
        foreach (var source in sources)
        {
            var requiresAuth = source.Capabilities.HasFlag(SourceCapabilities.RequiresAuth);
            var configured = requiresAuth && await credentials.ExistsAsync(source.Id, ct);
            summaries.Add(new SourceSummaryDto(
                source.Id, source.DisplayName, CapabilityNames(source.Capabilities), requiresAuth, configured));
        }

        return Results.Ok(summaries);
    }

    private static IResult GetCredentialFields(string id, ISourceRegistry registry)
    {
        if (registry.Get(id) is not ICredentialedSource credentialed)
        {
            return Results.BadRequest(new { error = "Source does not require credentials." });
        }

        var fields = credentialed.CredentialFields
            .Select(f => new CredentialFieldDto(f.Name, f.Label, f.Secret));
        return Results.Ok(fields);
    }

    private static async Task<IResult> SetCredentials(
        string id, Dictionary<string, string> values,
        ISourceRegistry registry, ISourceCredentialStore store, CancellationToken ct)
    {
        if (!registry.Contains(id))
        {
            return Results.NotFound();
        }

        await store.SetAsync(id, values, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> TestCredentials(string id, ISourceRegistry registry, CancellationToken ct)
    {
        if (registry.Get(id) is not ICredentialedSource credentialed)
        {
            return Results.BadRequest(new { error = "Source does not require credentials." });
        }

        var ok = await credentialed.ValidateCredentialsAsync(ct);
        return Results.Ok(new CredentialTestResult(ok));
    }

    private static async Task<IResult> GetTags(string id, CatalogService catalog, CancellationToken ct)
    {
        var tags = await catalog.GetTagsAsync(id, ct);
        return Results.Ok(tags.Select(t => new TagDto(t.Id, t.Name, t.Group)));
    }

    private static async Task<IResult> Search(
        string id, CatalogService catalog,
        string? q, int? limit, int? offset, string[]? lang, string[]? tag, string[]? rating, string? order,
        string? authorId, string? kind,
        CancellationToken ct)
    {
        var query = new SearchQuery
        {
            Text = q,
            Limit = Math.Clamp(limit ?? 20, 1, 100),
            Offset = Math.Max(0, offset ?? 0),
            TranslatedLanguages = lang ?? [],
            IncludedTags = tag ?? [],
            AuthorIds = string.IsNullOrWhiteSpace(authorId) ? [] : [authorId],
            ContentRatings = ParseRatings(rating),
            Order = ParseOrder(order),
        };

        // kind only matters for the aggregate "all" source (which sources to fan out to); a single
        // source already knows its own kind.
        var result = await catalog.SearchAsync(id, query, MediaKindQuery.Parse(kind), ct);
        return Results.Ok(new PagedDto<SeriesDto>(
            result.Items.Select(ApiMapper.ToDto).ToList(), result.Total, result.Limit, result.Offset));
    }

    private static SearchOrder ParseOrder(string? order) => order?.ToLowerInvariant() switch
    {
        "newest" => SearchOrder.Newest,
        "latest" => SearchOrder.LatestUploadedChapter,
        "title" => SearchOrder.Title,
        "rating" => SearchOrder.Rating,
        "followers" => SearchOrder.Followers,
        "year" => SearchOrder.Year,
        _ => SearchOrder.Relevance,
    };

    private static IReadOnlyList<ContentRating> ParseRatings(string[]? ratings) =>
        (ratings ?? [])
            .Select(r => Enum.TryParse<ContentRating>(r, ignoreCase: true, out var parsed) ? parsed : (ContentRating?)null)
            .Where(r => r is not null)
            .Select(r => r!.Value)
            .ToList();

    private static async Task<IResult> GetSeries(
        string id, string seriesId, CatalogService catalog, CancellationToken ct)
    {
        var series = await catalog.GetSeriesAsync(id, seriesId, ct);
        return series is null ? Results.NotFound() : Results.Ok(ApiMapper.ToDto(series));
    }

    private static async Task<IResult> GetChapters(
        string id, string seriesId, CatalogService catalog,
        string[]? lang, string[]? group, string? order, int? limit, int? offset, bool? includeExternal,
        CancellationToken ct)
    {
        var query = new ChapterQuery
        {
            TranslatedLanguages = lang ?? [],
            ScanlationGroups = group ?? [],
            Order = string.Equals(order, "desc", StringComparison.OrdinalIgnoreCase)
                ? ChapterOrder.ChapterDescending
                : ChapterOrder.ChapterAscending,
            Limit = Math.Clamp(limit ?? 100, 1, 500),
            Offset = Math.Max(0, offset ?? 0),
            IncludeExternal = includeExternal ?? false,
        };

        var result = await catalog.GetChaptersAsync(id, seriesId, query, ct);
        return Results.Ok(new PagedDto<ChapterDto>(
            result.Items.Select(ApiMapper.ToDto).ToList(), result.Total, result.Limit, result.Offset));
    }

    /// <summary>Preview reader: resolves a chapter's pages live from the source (no download) and
    /// returns the page count. Keyed by source id + source chapter id, not a library chapter id.</summary>
    private static async Task<IResult> GetChapterManifest(
        string id, string chapterId, CatalogService catalog, IMemoryCache cache, CancellationToken ct)
    {
        var set = await ResolvePreviewPagesAsync(cache, catalog, id, chapterId, ct);
        // Manga default; the reader lets the user flip direction, and there's no cheap per-chapter
        // signal for it here, so we don't pay a series fetch just to guess.
        return Results.Ok(new SourceChapterManifestDto(id, chapterId, set.Pages.Count, "rtl"));
    }

    /// <summary>Preview reader: proxies a single source page image server-side. Unlike the cover proxy,
    /// the URL comes from our own trusted page resolution (not user input), so there's no SSRF host
    /// allowlist — but we still proxy so the source's required per-image headers (Referer/User-Agent,
    /// which a browser &lt;img&gt; can't send) are applied, mirroring the download engine.</summary>
    private static async Task ProxyPage(
        HttpContext http, string id, string chapterId, int index,
        CatalogService catalog, IMemoryCache cache, IHttpClientFactory httpFactory, CancellationToken ct)
    {
        var set = await ResolvePreviewPagesAsync(cache, catalog, id, chapterId, ct);
        if (index < 0 || index >= set.Pages.Count)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var page = set.Pages[index];
        var client = httpFactory.CreateClient(DownloadOrchestrator.ImageClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, page.Url);
        var headers = page.Headers ?? set.Headers;
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var success = false;
        var cachedHit = false;
        long bytes = 0;
        try
        {
            using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            cachedHit = upstream.Headers.TryGetValues("X-Cache", out var values)
                        && values.Any(v => v.Contains("HIT", StringComparison.OrdinalIgnoreCase));
            if (!upstream.IsSuccessStatusCode)
            {
                http.Response.StatusCode = (int)upstream.StatusCode;
                return;
            }

            http.Response.ContentType = upstream.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            http.Response.Headers.CacheControl = "private, max-age=3600";
            bytes = upstream.Content.Headers.ContentLength ?? 0;
            success = true;
            await upstream.Content.CopyToAsync(http.Response.Body, ct);
        }
        finally
        {
            stopwatch.Stop();
            if (set.ReportAsync is not null)
            {
                try
                {
                    // MangaDex@Home success/failure reporting — best-effort, same as the download engine.
                    await set.ReportAsync(new PageReport(page.Url, success, cachedHit, bytes, stopwatch.Elapsed), ct);
                }
                catch
                {
                    // reporting is best-effort
                }
            }
        }
    }

    /// <summary>Resolves (and briefly caches) a chapter's page set. The manifest call and each per-page
    /// proxy call share the cache so we don't re-hit the source's resolution API (e.g. MangaDex@Home,
    /// whose base URLs are short-lived — hence the modest TTL) on every page.</summary>
    private static async Task<SourcePageSet> ResolvePreviewPagesAsync(
        IMemoryCache cache, CatalogService catalog, string sourceId, string chapterId, CancellationToken ct)
    {
        var key = $"preview:{sourceId}:{chapterId}:{PageQuality.Original}";
        if (cache.TryGetValue(key, out SourcePageSet? cached) && cached is not null)
        {
            return cached;
        }

        var set = await catalog.GetPagesAsync(sourceId, chapterId, PageQuality.Original, ct);
        cache.Set(key, set, TimeSpan.FromMinutes(5));
        return set;
    }

    /// <summary>Streams a source cover image (host-allowlisted) so the browser stays same-origin.</summary>
    private static async Task ProxyCover(
        HttpContext http, string id, IHttpClientFactory httpFactory, ISourceRegistry registry,
        ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var url = http.Request.Query["url"].ToString();
        if (string.IsNullOrEmpty(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!IsCoverHostAllowed(uri.Host, id, registry))
        {
            // Surface each newly-seen rejected host once (a blank thumbnail is otherwise silent), so an
            // admin can allowlist a trusted CDN — e.g. a new Photon-style image host — deliberately.
            if (LoggedRejectedCoverHosts.TryAdd(uri.Host, 0))
            {
                loggerFactory.CreateLogger("MangaFusion.CoverProxy").LogWarning(
                    "Cover proxy rejected unlisted host '{Host}' (source '{Source}'). If trusted, add it to "
                    + "AllowedCoverHosts or the source's ISourceCoverHosts. Example URL: {Url}", uri.Host, id, uri);
            }
            http.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Named client carries a User-Agent — MangaDex's CDN 400s requests without one.
        var client = httpFactory.CreateClient(CoverProxyClient);
        using var upstream = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!upstream.IsSuccessStatusCode)
        {
            http.Response.StatusCode = (int)upstream.StatusCode;
            return;
        }

        http.Response.ContentType = upstream.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
        http.Response.Headers.CacheControl = "public, max-age=86400";
        await upstream.Content.CopyToAsync(http.Response.Body, ct);
    }

    /// <summary>SSRF guard for the cover proxy: allow the fixed first-party CDNs, plus any host the
    /// <em>requesting</em> source declares via <see cref="ISourceCoverHosts"/> (scraper sources serve
    /// covers from their own domain). Never an open proxy — an unknown source/host is rejected.</summary>
    private static bool IsCoverHostAllowed(string host, string sourceId, ISourceRegistry registry)
    {
        if (AllowedCoverHosts.Contains(host))
        {
            return true;
        }

        return registry.Contains(sourceId)
               && registry.Get(sourceId) is ISourceCoverHosts coverHosts
               && coverHosts.CoverHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] CapabilityNames(SourceCapabilities caps) =>
        Enum.GetValues<SourceCapabilities>()
            .Where(c => c != SourceCapabilities.None && caps.HasFlag(c))
            .Select(c => c.ToString())
            .ToArray();
}
