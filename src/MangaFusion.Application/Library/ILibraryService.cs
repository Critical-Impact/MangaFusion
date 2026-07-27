using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Library;

/// <summary>A recently-downloaded chapter, one per series (most recent download wins). Feeds the
/// Home dashboard's "Recently downloaded" rail. Always readable — <see cref="ChapterId"/> is the
/// chapter the download completed for.</summary>
public sealed record RecentDownloadItem(
    Guid SeriesId,
    string SeriesTitle,
    string? CoverPath,
    Guid ChapterId,
    string? Number,
    string? Volume,
    DateTimeOffset DownloadedAt);

/// <summary>A series' most recently discovered chapter release, one per series. Feeds the Home
/// dashboard's "Recently updated" rail. <see cref="ChapterId"/> is null if that chapter hasn't been
/// downloaded yet — the UI should link to the series page instead of the reader in that case.</summary>
public sealed record RecentlyUpdatedItem(
    Guid SeriesId,
    string SeriesTitle,
    string? CoverPath,
    Guid? ChapterId,
    string? Number,
    string? Volume,
    DateTimeOffset UpdatedAt);

/// <summary>Persists and queries the shared library. Add-to-library pulls a source's series metadata
/// and full chapter feed, collapsing group variants into logical chapters + releases.</summary>
public interface ILibraryService
{
    /// <summary>Adds (or refreshes) a source series in the library; returns the library series id.</summary>
    Task<Guid> AddSeriesAsync(string sourceId, string sourceSeriesId, CancellationToken ct = default);

    /// <summary>Adds (or refreshes) a series' metadata from a metadata-only source (e.g. MangaUpdates)
    /// without requiring chapter capability and without touching chapters/artifacts. Returns the
    /// library series id.</summary>
    /// <summary><paramref name="createKind"/> pins the library a <em>newly-created</em> series lands in
    /// (used by the import wizard, where the batch kind is the user's explicit choice); null falls back to
    /// the source's per-series kind hint. Ignored for an already-linked series (its kind is fixed).</summary>
    Task<Guid> AddOrUpdateMetadataOnlyAsync(
        string sourceId, string sourceSeriesId, MediaKind? createKind = null, CancellationToken ct = default);

    /// <summary>Re-fetches a series' metadata from its metadata-primary source. Throws if it has no
    /// external metadata source to refresh from.</summary>
    Task RefreshMetadataAsync(Guid seriesId, CancellationToken ct = default);

    /// <summary>Searches/filters/sorts/paginates the library for the browse UI.</summary>
    Task<LibraryPage> QueryLibraryAsync(LibraryQuery query, CancellationToken ct = default);

    /// <summary>Distinct tags actually in use across one library, optionally restricted to one group
    /// (manga: "genre"/"theme"/…; comics: "publisher"/"character"/…) — feeds the filter dropdowns.</summary>
    Task<IReadOnlyList<TagInfo>> GetLibraryTagsAsync(
        MediaKind kind, string? group = null, CancellationToken ct = default);

    /// <summary>Every known tag for one library regardless of whether it's currently used by any series —
    /// feeds the local-import tag picker, which should offer known tags before they're attached to anything.</summary>
    Task<IReadOnlyList<TagInfo>> GetTagCatalogAsync(MediaKind kind, CancellationToken ct = default);

    /// <summary>Upserts a source's full tag registry into the local Tag catalog. Idempotent (safe to
    /// run on every boot) and a no-op if the source isn't registered. Populates the genre/theme
    /// dropdowns and the local-import tag picker even before any series from that source is imported.</summary>
    Task SyncSourceTagsAsync(string sourceId, CancellationToken ct = default);

    /// <summary>The locally-cached copy of a source's tag registry (from <see cref="SyncSourceTagsAsync"/>),
    /// keyed by the source's own tag id — lets browse UIs (e.g. the genre/theme chip browser) read from
    /// our DB instead of hitting the source's API on every page load. Empty if never synced.</summary>
    Task<IReadOnlyList<SourceTag>> GetCachedSourceTagsAsync(string sourceId, CancellationToken ct = default);

    /// <summary>Every series' id + title, unpaginated — for id-to-title lookups (e.g. the activity feed)
    /// and the import merge-target picker, not for the browse UI which goes through
    /// <see cref="QueryLibraryAsync"/>. Pass a <paramref name="kind"/> to scope to one library (the merge
    /// picker must, so a manga import can't offer a light-novel series as a merge target); null returns all.</summary>
    Task<IReadOnlyList<(Guid Id, string Title)>> GetLibraryTitlesAsync(
        MediaKind? kind = null, CancellationToken ct = default);

    /// <summary>Which of the given source refs are already in the library, mapped to their library series
    /// id — lets the browse grid mark already-added series (and link straight to them) without a query per
    /// card. A ref with no matching library series is simply absent from the result.</summary>
    Task<IReadOnlyList<(string SourceId, string SourceSeriesId, Guid LibraryId)>> ResolveLibraryLinksAsync(
        IReadOnlyCollection<(string SourceId, string SourceSeriesId)> refs, CancellationToken ct = default);

    /// <summary>Absolute path to the series' cached cover, or null if none. Resolved here (not by the
    /// caller) because a stored cover path is relative to its own library's root.</summary>
    Task<string?> GetCoverFileAsync(Guid seriesId, CancellationToken ct = default);

    /// <summary>Series with chapters, releases, and artifact links loaded; null if not in library.</summary>
    Task<Series?> GetSeriesAsync(Guid seriesId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<Guid, ReadingProgress>> GetProgressAsync(
        Guid userId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Sets the series' ordered scanlation-group preference (shared/admin setting).</summary>
    Task SetPreferredGroupsAsync(Guid seriesId, IReadOnlyList<string> groups, CancellationToken ct = default);

    Task SetPolicyAsync(
        Guid seriesId, int? gracePeriodDays, bool autoDownload, IReadOnlyList<string> languages,
        CancellationToken ct = default);

    Task<Follow?> GetFollowAsync(Guid userId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Which of <paramref name="seriesIds"/> the user follows, in one query. The library grid needs
    /// a followed flag per row and nothing else about the follow, so asking <see cref="GetFollowAsync"/> per
    /// row would issue a query per row (up to 100 a page) to fetch rows it then throws away.</summary>
    Task<IReadOnlySet<Guid>> GetFollowedSeriesIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default);

    Task<Follow> FollowAsync(
        Guid userId, Guid seriesId, IReadOnlyList<string> languages, bool autoDownload, CancellationToken ct = default);

    Task UnfollowAsync(Guid userId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Most recently completed downloads, one per series (most recent wins) — feeds the Home
    /// dashboard's "Recently downloaded" rail. <paramref name="kind"/> null = both libraries (the user's
    /// Home preference).</summary>
    Task<IReadOnlyList<RecentDownloadItem>> GetRecentDownloadsAsync(
        MediaKind? kind, int limit, CancellationToken ct = default);

    /// <summary>Series with the most recently discovered chapter releases, one per series — feeds the
    /// Home dashboard's "Recently updated" rail. <paramref name="kind"/> null = both libraries (the user's
    /// Home preference).</summary>
    Task<IReadOnlyList<RecentlyUpdatedItem>> GetRecentlyUpdatedAsync(
        MediaKind? kind, int limit, CancellationToken ct = default);

    /// <summary>Permanently deletes a series: every chapter, release, artifact (on-disk files too),
    /// follow, reading progress, and source link. Cannot be undone.</summary>
    Task DeleteSeriesAsync(Guid seriesId, CancellationToken ct = default);

    /// <summary>Permanently deletes one chapter and its releases. Any artifact this chapter was the
    /// sole remaining link for is deleted too (DB row + on-disk file); an artifact still shared with
    /// other chapters (a multi-chapter volume file) is left in place for them. Cannot be undone.</summary>
    Task DeleteChapterAsync(Guid chapterId, CancellationToken ct = default);

    /// <summary>Edits a manually-imported chapter's number/volume/title, recomputing its sort key.
    /// Throws if the chapter isn't manually imported, or if the new number/volume collides with
    /// another chapter in the same series+language.</summary>
    Task UpdateChapterAsync(
        Guid chapterId, string? number, string? volume, string? title, CancellationToken ct = default);

    /// <summary>Switches a series' chapter ordering mode, recomputing every existing chapter's sort
    /// key accordingly. A no-op if the series is already in that mode. Throws (without changing
    /// anything) if the switch would merge two existing chapters onto the same identity key.</summary>
    Task SetChapterSortModeAsync(Guid seriesId, ChapterSortMode mode, CancellationToken ct = default);

    /// <summary>Manually sets a series' title/year/description and locks all three against being
    /// overwritten by a future metadata refresh or monitor scan, until <see cref="UnlockMetadataAsync"/>
    /// is called.</summary>
    Task UpdateSeriesMetadataAsync(
        Guid seriesId, string title, int? year, string? description, CancellationToken ct = default);

    /// <summary>Clears the title/year/description lock — the next metadata refresh/monitor scan will
    /// overwrite them from the source again.</summary>
    Task UnlockMetadataAsync(Guid seriesId, CancellationToken ct = default);

    /// <summary>Validates and stores a user-uploaded cover image, locking it against being overwritten
    /// by a future metadata refresh. Returns false if the series doesn't exist or the image is
    /// invalid.</summary>
    Task<bool> SetCustomCoverAsync(Guid seriesId, Stream image, CancellationToken ct = default);

    /// <summary>Clears the cover lock — the next metadata refresh will re-download the source's cover
    /// again.</summary>
    Task UnlockCoverAsync(Guid seriesId, CancellationToken ct = default);
}
