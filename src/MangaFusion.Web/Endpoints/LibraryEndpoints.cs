using System.Security.Claims;
using MangaFusion.Application.Downloads;
using MangaFusion.Application.Library;
using MangaFusion.Application.Reading;
using MangaFusion.Application.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Web.Models;

namespace MangaFusion.Web.Endpoints;

/// <summary>Shared-library browsing + add-to-library + follow endpoints.</summary>
public static class LibraryEndpoints
{
    public static void MapLibraryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library").RequireAuthorization();

        group.MapPost("/series", AddSeries);
        group.MapPost("/series/membership", Membership);
        group.MapGet("/series", ListSeries);
        group.MapGet("/series/titles", GetTitles);
        group.MapGet("/tags", GetTags);
        group.MapGet("/tags/catalog", GetTagCatalog);
        group.MapGet("/series/{id:guid}", GetSeries);
        group.MapGet("/series/{id:guid}/cover", GetCover);
        group.MapPost("/series/{id:guid}/follow", Follow);
        group.MapDelete("/series/{id:guid}/follow", Unfollow);
        group.MapPost("/series/{id:guid}/refresh-metadata", RefreshMetadata).RequireAuthorization("Admin");
        group.MapDelete("/series/{id:guid}", DeleteSeries).RequireAuthorization("Admin");
        group.MapDelete("/chapters/{id:guid}", DeleteChapter).RequireAuthorization("Admin");
        group.MapPatch("/chapters/{id:guid}", UpdateChapter).RequireAuthorization("Admin");

        group.MapPost("/chapters/{id:guid}/download", QueueDownload);
        group.MapPost("/series/{id:guid}/download-missing", QueueMissing);
        group.MapGet("/downloads", ListDownloads);
        group.MapGet("/recent-downloads", RecentDownloads);
        group.MapGet("/recently-updated", RecentlyUpdated);

        group.MapPut("/series/{id:guid}/groups", SetGroups).RequireAuthorization("Admin");
        group.MapPut("/series/{id:guid}/policy", SetPolicy).RequireAuthorization("Admin");
        group.MapPut("/series/{id:guid}/sort-mode", SetSortMode).RequireAuthorization("Admin");

        group.MapPatch("/series/{id:guid}", UpdateSeriesMetadata).RequireAuthorization("Admin");
        group.MapDelete("/series/{id:guid}/metadata-lock", UnlockMetadata).RequireAuthorization("Admin");
        group.MapPost("/series/{id:guid}/cover", UploadCover).RequireAuthorization("Admin").DisableAntiforgery();
        group.MapDelete("/series/{id:guid}/cover-lock", UnlockCover).RequireAuthorization("Admin");
    }

    private static async Task<IResult> SetSortMode(
        Guid id, SetSortModeRequest request, ILibraryService library, CancellationToken ct)
    {
        if (!Enum.TryParse<ChapterSortMode>(request.SortMode, ignoreCase: true, out var mode))
        {
            return Results.BadRequest(new { error = $"Unknown sort mode '{request.SortMode}'." });
        }

        try
        {
            await library.SetChapterSortModeAsync(id, mode, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> QueueMissing(
        Guid id, DownloadMissingRequest? request, IDownloadService downloads, CancellationToken ct)
    {
        try
        {
            var count = await downloads.QueueSeriesMissingAsync(id, request?.Languages ?? [], ct);
            return Results.Ok(new { queued = count });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetGroups(
        Guid id, SetGroupsRequest request, ILibraryService library, CancellationToken ct)
    {
        await library.SetPreferredGroupsAsync(id, request.Groups ?? [], ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetPolicy(
        Guid id, SetPolicyRequest request, ILibraryService library, CancellationToken ct)
    {
        var languages = request.Languages ?? [];
        if (ValidateLanguages(languages) is { } error) return error;

        await library.SetPolicyAsync(id, request.GracePeriodDays, request.AutoDownload, languages, ct);
        return Results.NoContent();
    }

    private static IResult? ValidateLanguages(IEnumerable<string> languages)
    {
        var unknown = languages.FirstOrDefault(l => !MangaLanguage.IsKnown(l));
        return unknown is null ? null : Results.BadRequest($"Unknown language '{unknown}'.");
    }

    private static async Task<IResult> QueueDownload(
        Guid id, DownloadChapterRequest? request, IDownloadService downloads, CancellationToken ct)
    {
        try
        {
            var downloadId = await downloads.QueueChapterDownloadAsync(id, request?.ReleaseId, ct);
            return Results.Ok(new { downloadId });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListDownloads(IDownloadService downloads, CancellationToken ct)
    {
        var recent = await downloads.GetRecentAsync(ct: ct);
        return Results.Ok(recent.Select(d => new DownloadDto(
            d.Id, d.SeriesId, d.ChapterId, d.Description, d.Status.ToString(),
            d.PagesDone, d.PagesTotal, d.Error, d.CreatedAt)));
    }

    private static async Task<IResult> RecentDownloads(
        ILibraryService library, string? kind, int? limit, CancellationToken ct)
    {
        var take = limit is > 0 and <= 50 ? limit.Value : 12;
        var items = await library.GetRecentDownloadsAsync(MediaKindQuery.ParseOptional(kind), take, ct);
        return Results.Ok(items.Select(i => new
        {
            i.SeriesId,
            i.SeriesTitle,
            coverUrl = i.CoverPath is null ? null : $"/api/library/series/{i.SeriesId}/cover",
            i.ChapterId,
            i.Number,
            i.Volume,
            i.DownloadedAt,
        }));
    }

    private static async Task<IResult> RecentlyUpdated(
        ILibraryService library, string? kind, int? limit, CancellationToken ct)
    {
        var take = limit is > 0 and <= 50 ? limit.Value : 12;
        var items = await library.GetRecentlyUpdatedAsync(MediaKindQuery.ParseOptional(kind), take, ct);
        return Results.Ok(items.Select(i => new
        {
            i.SeriesId,
            i.SeriesTitle,
            coverUrl = i.CoverPath is null ? null : $"/api/library/series/{i.SeriesId}/cover",
            i.ChapterId,
            i.Number,
            i.Volume,
            i.UpdatedAt,
        }));
    }

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> AddSeries(
        AddSeriesRequest request, ILibraryService library, ISourceRegistry registry, CancellationToken ct)
    {
        // A source with no chapter feed (MangaUpdates, ComicVine) can still seed a library entry — it
        // just contributes metadata, with the chapters arriving later from a local import.
        var hasChapters = registry.Get(request.SourceId).Capabilities
            .HasFlag(Contracts.Models.SourceCapabilities.Chapters);

        var id = hasChapters
            ? await library.AddSeriesAsync(request.SourceId, request.SourceSeriesId, ct)
            : await library.AddOrUpdateMetadataOnlyAsync(request.SourceId, request.SourceSeriesId, ct);

        return Results.Ok(new { id });
    }

    /// <summary><paramref name="tags"/> carries the tag filters: one repetition per facet, each a
    /// comma-separated id list — <c>?tags=a,b&amp;tags=c</c> means "(a OR b) AND c". Which facets exist is
    /// the caller's business (genre/theme for manga, publisher/character/concept for comics); filtering
    /// only needs the ids.</summary>
    private static async Task<IResult> ListSeries(
        ClaimsPrincipal user, ILibraryService library, CancellationToken ct,
        string? kind, string? q, string[]? tags, string? rating, string? sort, string? order,
        int? limit, int? offset, string? authorSourceId, string? authorId, string? sourceId)
    {
        var userId = CurrentUser(user);
        var query = new LibraryQuery(
            MediaKindQuery.Parse(kind),
            q,
            ParseTagFacets(tags),
            ParseRating(rating),
            sort?.ToLowerInvariant() is "added" or "year" or "chapters" ? sort.ToLowerInvariant() : "title",
            order?.ToLowerInvariant() == "desc" ? "desc" : "asc",
            Math.Clamp(limit ?? 24, 1, 100),
            Math.Max(0, offset ?? 0),
            AuthorSourceId: authorSourceId,
            AuthorNativeId: authorId,
            SourceId: sourceId);

        var result = await library.QueryLibraryAsync(query, ct);

        // One query for the whole page, not one per row.
        var followed = await library.GetFollowedSeriesIdsAsync(
            userId, result.Items.Select(s => s.Id).ToList(), ct);

        var dtos = result.Items
            .Select(s => new LibrarySeriesDto(
                s.Id, s.Title, CoverUrl(s.Id, s.CoverPath, s.CoverUpdatedAt), followed.Contains(s.Id), s.Tags,
                s.Year, s.AddedAt, s.ChapterCount, s.Sources))
            .ToList();

        return Results.Ok(new PagedDto<LibrarySeriesDto>(dtos, result.Total, query.Limit, query.Offset));
    }

    private static async Task<IResult> RefreshMetadata(Guid id, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.RefreshMetadataAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteSeries(Guid id, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.DeleteSeriesAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> DeleteChapter(Guid id, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.DeleteChapterAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateChapter(
        Guid id, UpdateChapterRequest request, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.UpdateChapterAsync(id, request.Number, request.Volume, request.Title, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateSeriesMetadata(
        Guid id, UpdateSeriesMetadataRequest request, ILibraryService library, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { error = "Title is required." });
        }

        try
        {
            await library.UpdateSeriesMetadataAsync(id, request.Title, request.Year, request.Description, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UnlockMetadata(Guid id, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.UnlockMetadataAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UploadCover(
        Guid id, ILibraryService library, HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Expected a multipart form upload." });
        }

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest(new { error = "No image supplied." });
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var ok = await library.SetCustomCoverAsync(id, stream, ct);
            return ok ? Results.NoContent() : Results.BadRequest(new { error = "Series not found or the image was invalid." });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UnlockCover(Guid id, ILibraryService library, CancellationToken ct)
    {
        try
        {
            await library.UnlockCoverAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IReadOnlyList<IReadOnlyList<Guid>> ParseTagFacets(string[]? tags) =>
        (tags ?? [])
            .Select(facet => (IReadOnlyList<Guid>)facet
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(id => Guid.TryParse(id, out var parsed) ? parsed : (Guid?)null)
                .Where(id => id is not null)
                .Select(id => id!.Value)
                .ToList())
            .Where(facet => facet.Count > 0)
            .ToList();

    private static ContentRating? ParseRating(string? value) =>
        Enum.TryParse<ContentRating>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static async Task<IResult> GetTags(
        ILibraryService library, string? kind, string? group, CancellationToken ct) =>
        Results.Ok(await library.GetLibraryTagsAsync(MediaKindQuery.Parse(kind), group, ct));

    private static async Task<IResult> GetTagCatalog(
        ILibraryService library, string? kind, CancellationToken ct) =>
        Results.Ok(await library.GetTagCatalogAsync(MediaKindQuery.Parse(kind), ct));

    private static async Task<IResult> GetTitles(ILibraryService library, CancellationToken ct) =>
        Results.Ok((await library.GetLibraryTitlesAsync(ct)).Select(s => new LibraryTitleDto(s.Id, s.Title)));

    // Batch membership check for the browse grid: which of these source series are already in the
    // library, and under which library id (so the card can link straight there).
    private static async Task<IResult> Membership(
        LibraryMembershipRequest request, ILibraryService library, CancellationToken ct)
    {
        var refs = (request.Refs ?? [])
            .Select(r => (r.SourceId, r.SourceSeriesId))
            .ToList();
        if (refs.Count == 0) return Results.Ok(Array.Empty<LibraryMembershipDto>());

        var matches = await library.ResolveLibraryLinksAsync(refs, ct);
        return Results.Ok(matches.Select(m => new LibraryMembershipDto(m.SourceId, m.SourceSeriesId, m.LibraryId)));
    }

    private static async Task<IResult> GetSeries(
        Guid id, ClaimsPrincipal user, ILibraryService library, IReaderService reader,
        ISourceRegistry registry, CancellationToken ct)
    {
        var series = await library.GetSeriesAsync(id, ct);
        if (series is null)
        {
            return Results.NotFound();
        }

        var userId = CurrentUser(user);
        var progress = await library.GetProgressAsync(userId, id, ct);
        var follow = await library.GetFollowAsync(userId, id, ct);
        var reading = await reader.IsReadingAsync(userId, id, ct);

        return Results.Ok(ToDetail(series, progress, follow, reading, registry));
    }

    private static async Task GetCover(
        Guid id, ILibraryService library, HttpContext http, CancellationToken ct)
    {
        var file = await library.GetCoverFileAsync(id, ct);
        if (file is null || !File.Exists(file))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // The `v=` query param (see CoverUrl) changes whenever the file does, so a cache hit on that
        // exact URL is always the right bytes — safe to cache aggressively. A few call sites (recent
        // downloads/updates, reader "up next", collection member thumbnails) still request the bare
        // unversioned URL, so those get a short cache instead of a stale-forever one.
        http.Response.ContentType = "image/jpeg";
        http.Response.Headers.CacheControl = http.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "public, max-age=300";
        await http.Response.SendFileAsync(file, ct);
    }

    private static async Task<IResult> Follow(
        Guid id, FollowRequest request, ClaimsPrincipal user, ILibraryService library, CancellationToken ct)
    {
        var languages = request.Languages ?? [];
        if (ValidateLanguages(languages) is { } error) return error;

        var follow = await library.FollowAsync(CurrentUser(user), id, languages, request.AutoDownload, ct);
        return Results.Ok(new { follow.Id, follow.Languages, follow.AutoDownload });
    }

    private static async Task<IResult> Unfollow(
        Guid id, ClaimsPrincipal user, ILibraryService library, CancellationToken ct)
    {
        await library.UnfollowAsync(CurrentUser(user), id, ct);
        return Results.NoContent();
    }

    // The cover file is overwritten in place at a stable path, so the URL needs its own cache-busting
    // version — otherwise browsers keep serving a stale cached image after a cover change (until a
    // hard refresh bypasses the disk cache).
    private static string? CoverUrl(Guid id, string? coverPath, DateTimeOffset? coverUpdatedAt) =>
        coverPath is null
            ? null
            : coverUpdatedAt is { } updatedAt
                ? $"/api/library/series/{id}/cover?v={updatedAt.UtcTicks}"
                : $"/api/library/series/{id}/cover";

    private static LibrarySeriesDetailDto ToDetail(
        Series series, IReadOnlyDictionary<Guid, ReadingProgress> progress, Follow? follow, bool reading,
        ISourceRegistry registry)
    {
        var link = series.SourceLinks.FirstOrDefault(l => l.IsMetadataPrimary)
                   ?? series.SourceLinks.FirstOrDefault();

        // Friendly source name for display (e.g. "WitchScans"); falls back to the id, then null.
        var sourceName = link?.SourceId is { } sourceId
            ? registry.Contains(sourceId) ? registry.Get(sourceId).DisplayName : sourceId
            : null;

        var chapters = OrderChapters(series.Chapters, series.SortMode)
            .Select(c => ToChapter(c, progress))
            .ToList();

        return new LibrarySeriesDetailDto(
            series.Id,
            series.Title,
            series.AltTitles,
            series.Description,
            CoverUrl(series.Id, series.CoverPath, series.CoverUpdatedAt),
            series.Authors.Select(a => new AuthorRefDto(a.SourceId, a.SourceAuthorId, a.Name)).ToList(),
            series.Tags.Select(t => new TagInfo(t.Id, t.Name, t.Group, t.SourceId, t.SourceTagId)).ToList(),
            series.ContentRating.ToString(),
            series.Status.ToString(),
            series.Year,
            series.PreferredGroups,
            series.AutoDownload,
            series.GracePeriodDays,
            series.Languages,
            series.LastScannedAt,
            link?.SourceId,
            sourceName,
            link?.SourceSeriesId,
            series.SiteUrl,
            follow is not null,
            follow?.AutoDownload ?? false,
            follow?.Languages ?? [],
            reading,
            chapters,
            series.SortMode.ToString(),
            series.LockedFields.HasFlag(SeriesLockedFields.Title),
            series.LockedFields.HasFlag(SeriesLockedFields.Year),
            series.LockedFields.HasFlag(SeriesLockedFields.Description),
            series.LockedFields.HasFlag(SeriesLockedFields.Cover));
    }

    /// <summary>Orders a series' chapters for display. <see cref="ChapterSortMode.Absolute"/> (the
    /// default) sorts purely by NumberSort/NumberKey as always.
    /// <see cref="ChapterSortMode.VolumeThenChapter"/> sorts by volume first, then chapter number —
    /// the whole-volume row itself (blank Number) always sorts first within its own volume, since the
    /// existing NumberKey uniqueness constraint already guarantees a blank-Number row is unique per
    /// volume (see ChapterNumber.QualifyKey's doc comment).</summary>
    private static IEnumerable<Chapter> OrderChapters(IEnumerable<Chapter> chapters, ChapterSortMode mode) =>
        mode == ChapterSortMode.VolumeThenChapter
            ? chapters.OrderBy(c => c.Language)
                .ThenBy(c => c.VolumeSort ?? decimal.MaxValue)
                .ThenBy(c => c.Number == null ? 0 : 1)
                .ThenBy(c => c.NumberSort ?? decimal.MaxValue)
                .ThenBy(c => c.NumberKey)
            : chapters.OrderBy(c => c.Language)
                .ThenBy(c => c.NumberSort ?? decimal.MaxValue)
                .ThenBy(c => c.NumberKey);

    private static LibraryChapterDto ToChapter(Chapter c, IReadOnlyDictionary<Guid, ReadingProgress> progress)
    {
        progress.TryGetValue(c.Id, out var p);
        var activeRelease = c.ActiveRelease ?? c.Releases.FirstOrDefault(r => r.Id == c.ActiveReleaseId);
        var activeGroup = activeRelease?.GroupKey;

        // Chapter date = the active release's publish date, else the most recent release's.
        var publishedAt = activeRelease?.PublishedAt
            ?? c.Releases.OrderByDescending(r => r.PublishedAt).FirstOrDefault()?.PublishedAt;

        return new LibraryChapterDto(
            c.Id,
            c.Language,
            c.Number,
            c.NumberSort,
            c.Volume,
            c.VolumeSort,
            c.Title,
            c.ActiveArtifactId is not null,
            activeGroup,
            p?.PageIndex ?? 0,
            p?.Completed ?? false,
            publishedAt,
            c.Releases
                .OrderByDescending(r => r.PublishedAt)
                .Select(r => new ReleaseDto(r.Id, r.ScanlationGroups, r.GroupKey, r.IsExternal, r.PublishedAt, r.PageCount))
                .ToList(),
            activeRelease?.SourceId == LocalSourceConstants.SourceId);
    }
}
