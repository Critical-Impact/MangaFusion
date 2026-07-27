using System.Linq.Expressions;
using Hangfire;
using MangaFusion.Application.Library;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

public sealed class LibraryService(
    AppDbContext db,
    ISourceRegistry registry,
    ChapterImporter importer,
    SeriesMetadataApplier metadataApplier,
    SeriesCoverCache coverCache,
    TagResolver tagResolver,
    LibraryPaths paths) : ILibraryService
{
    public const string ImageClientName = "source-images";

    private const int FeedPageSize = 500;
    private const int MaxFeedPages = 20; // safety cap: 20 * 500 = 10k releases

    public async Task<Guid> AddSeriesAsync(string sourceId, string sourceSeriesId, CancellationToken ct = default)
    {
        var chapterSource = registry.GetChapterSource(sourceId);

        var (series, sourceSeries) = await ApplySourceMetadataAsync(sourceId, sourceSeriesId, createKind: null, ct);

        var sourceChapters = await FetchAllChaptersAsync(chapterSource, sourceSeriesId, ct);
        await importer.ImportAsync(series, sourceChapters, ct);
        series.LastScannedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        await TryCacheCoverAsync(series, sourceSeries, ct);
        await db.SaveChangesAsync(ct);

        return series.Id;
    }

    /// <summary>Adds (or refreshes) a series' metadata from a metadata-only source (no chapter
    /// capability required) — used by the MangaUpdates-assisted import wizard and by re-fetching
    /// metadata for an already-imported series. Never touches chapters/artifacts.</summary>
    public async Task<Guid> AddOrUpdateMetadataOnlyAsync(
        string sourceId, string sourceSeriesId, MediaKind? createKind = null, CancellationToken ct = default)
    {
        var (series, sourceSeries) = await ApplySourceMetadataAsync(sourceId, sourceSeriesId, createKind, ct);
        await db.SaveChangesAsync(ct);

        await TryCacheCoverAsync(series, sourceSeries, ct);
        await db.SaveChangesAsync(ct);

        return series.Id;
    }

    /// <summary>Finds-or-creates the library series linked to (sourceId, sourceSeriesId) and applies
    /// the source's current metadata + cover. Shared by the full chapter-fetching add flow and the
    /// metadata-only flow; callers are responsible for their own <see cref="AppDbContext.SaveChangesAsync"/>.</summary>
    private async Task<(Series Series, SourceSeries SourceSeries)> ApplySourceMetadataAsync(
        string sourceId, string sourceSeriesId, MediaKind? createKind, CancellationToken ct)
    {
        var metaSource = registry.GetMetadataSource(sourceId);
        var sourceSeries = await metaSource.GetSeriesAsync(sourceSeriesId, ct)
            ?? throw new InvalidOperationException($"Series '{sourceSeriesId}' not found on source '{sourceId}'.");

        // A series added from a source lands in whichever library that series belongs to. When the
        // caller knows the target library — the import wizard, whose batch kind (mode + per-kind inbox)
        // is the user's explicit choice — that wins: a MangaUpdates match only supplies metadata, it
        // doesn't get to re-decide the library out from under a light-novel import whose matched entry
        // isn't typed "Novel". Otherwise fall back to the source's per-series hint (KindOf), which
        // routes a MangaUpdates "Novel" into the light-novel library and everything else into manga.
        // Either way the kind is authoritative for *resolution* too: one source entry may back a series
        // per kind (a shared MangaUpdates id for a manga and its LN adaptation), so the find is scoped by
        // kind — without it a light-novel import would reuse the manga series and collide on chapters.
        var effectiveKind = createKind ?? MediaKinds.KindOf(metaSource, sourceSeries);

        var series = await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Tags)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .FirstOrDefaultAsync(
                s => s.Kind == effectiveKind
                     && s.SourceLinks.Any(l => l.SourceId == sourceId && l.SourceSeriesId == sourceSeriesId), ct);

        if (series is null)
        {
            series = new Series { Kind = effectiveKind };
            series.SourceLinks.Add(new SeriesSourceLink
            {
                SourceId = sourceId,
                SourceSeriesId = sourceSeriesId,
                Kind = effectiveKind,
                IsMetadataPrimary = true,
            });
            db.Series.Add(series);
        }

        await metadataApplier.ApplyAsync(series, sourceSeries, ct);
        return (series, sourceSeries);
    }

    public async Task<LibraryPage> QueryLibraryAsync(LibraryQuery query, CancellationToken ct = default)
    {
        var q = db.Series.Where(s => s.Kind == query.Kind);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // SQLite's EF provider translates .Contains() via instr(), which is case-sensitive;
            // lowering both sides gives portable case-insensitive matching (SQLite and Postgres alike).
            var needle = query.Search.ToLowerInvariant();
            q = q.Where(s => s.Title.ToLower().Contains(needle));
        }

        // Tags is a real many-to-many, so tag filtering, sorting (including by chapter count), and paging
        // all translate to SQL — no more materialize-then-filter workaround. One Where per facet is what
        // makes facets AND together while staying an OR within each.
        foreach (var facet in query.TagFacets.Where(f => f.Count > 0))
        {
            var ids = facet;
            q = q.Where(s => s.Tags.Any(t => ids.Contains(t.Id)));
        }

        if (query.Rating is { } rating)
        {
            q = q.Where(s => s.ContentRating == rating);
        }

        if (query.AuthorSourceId is { } authorSourceId && query.AuthorNativeId is { } authorNativeId)
        {
            // "local" authors have no SourceId/SourceAuthorId (name-only resolution, see
            // AuthorResolver.ResolveOrCreateByNameAsync) — match by name instead of native id.
            q = authorSourceId == "local"
                ? q.Where(s =>
                    s.Authors.Any(a => a.SourceId == null && a.Name == authorNativeId) ||
                    s.Artists.Any(a => a.SourceId == null && a.Name == authorNativeId))
                : q.Where(s =>
                    s.Authors.Any(a => a.SourceId == authorSourceId && a.SourceAuthorId == authorNativeId) ||
                    s.Artists.Any(a => a.SourceId == authorSourceId && a.SourceAuthorId == authorNativeId));
        }

        if (query.SourceId is { } sourceId)
        {
            q = q.Where(s => s.SourceLinks.Any(l => l.SourceId == sourceId));
        }

        q = Sort(q, query.Sort, query.Order);

        var total = await q.CountAsync(ct);
        var page = await q
            .Skip(query.Offset).Take(query.Limit)
            .Select(s => new LibraryListItem(
                s.Id, s.Title, s.CoverPath, s.CoverUpdatedAt, s.Tags.Select(t => t.Name).ToList(), s.Year,
                s.AddedAt, s.Chapters.Count, s.SourceLinks.Select(l => l.SourceId).ToList()))
            .ToListAsync(ct);

        return new LibraryPage(page, total);
    }

    /// <summary>Re-fetches a series' metadata from its metadata-primary source (e.g. re-pulling
    /// MangaUpdates data after import). Throws if the series has no external metadata source (a
    /// "local"-only series has nothing to refresh from).</summary>
    public async Task RefreshMetadataAsync(Guid seriesId, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.SourceLinks)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        var link = series.SourceLinks.FirstOrDefault(l => l.IsMetadataPrimary && l.SourceId != LocalSourceConstants.SourceId)
            ?? series.SourceLinks.FirstOrDefault(l => l.SourceId != LocalSourceConstants.SourceId);
        if (link is null)
        {
            throw new InvalidOperationException("This series has no external metadata source to refresh from.");
        }

        // Pass the series' own kind, not KindOf's guess: a MangaUpdates "Novel"-typed entry can back a
        // manga series, and re-deriving the kind here would re-resolve to (or create) the wrong one.
        await AddOrUpdateMetadataOnlyAsync(link.SourceId, link.SourceSeriesId, series.Kind, ct);
    }

    private static IQueryable<Series> Sort(IQueryable<Series> items, string sort, string order)
    {
        IOrderedQueryable<Series> Ordered<TKey>(Expression<Func<Series, TKey>> key) =>
            order == "desc" ? items.OrderByDescending(key) : items.OrderBy(key);

        return sort switch
        {
            "added" => Ordered(s => s.AddedAt),
            "year" => Ordered(s => s.Year ?? int.MinValue),
            "chapters" => Ordered(s => s.Chapters.Count),
            _ => Ordered(s => s.Title),
        };
    }

    public async Task<IReadOnlyList<TagInfo>> GetLibraryTagsAsync(
        MediaKind kind, string? group = null, CancellationToken ct = default)
    {
        var q = db.Tags.Where(t => t.Kind == kind && t.Series.Any());
        if (group is not null)
        {
            q = q.Where(t => t.Group == group);
        }

        return await q.OrderBy(t => t.Name)
            .Select(t => new TagInfo(t.Id, t.Name, t.Group, t.SourceId, t.SourceTagId))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TagInfo>> GetTagCatalogAsync(
        MediaKind kind, CancellationToken ct = default) =>
        await db.Tags.Where(t => t.Kind == kind)
            .OrderBy(t => t.Group).ThenBy(t => t.Name)
            .Select(t => new TagInfo(t.Id, t.Name, t.Group, t.SourceId, t.SourceTagId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<SourceTag>> GetCachedSourceTagsAsync(
        string sourceId, CancellationToken ct = default) =>
        await db.Tags
            .Where(t => t.SourceId == sourceId && t.SourceTagId != null)
            .OrderBy(t => t.Name)
            .Select(t => new SourceTag(t.SourceTagId!, t.Name, t.Group))
            .ToListAsync(ct);

    [AutomaticRetry(Attempts = 3)]
    public async Task SyncSourceTagsAsync(string sourceId, CancellationToken ct = default)
    {
        if (!registry.Contains(sourceId))
        {
            return;
        }

        var tags = await registry.GetMetadataSource(sourceId).GetTagsAsync(ct);
        if (tags.Count == 0)
        {
            return;
        }

        // A source's tag registry belongs to every library it serves — for a multi-kind source
        // (MangaUpdates spans manga + light novels) the same genre taxonomy has to exist under each kind so
        // its facet filters populate in both. Tag.Kind deliberately duplicates rows per kind, so this is a
        // per-kind resolve, not MangaUpdates-specific code.
        var refs = tags.Select(t => new SourceTagRef(t.Id, t.Name, t.Group)).ToList();
        foreach (var kind in registry.Get(sourceId).SupportedKinds)
        {
            await tagResolver.ResolveSourceTagsAsync(MediaKinds.ToDomain(kind), sourceId, refs, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid Id, string Title)>> GetLibraryTitlesAsync(
        MediaKind? kind = null, CancellationToken ct = default) =>
        (await db.Series
                .Where(s => kind == null || s.Kind == kind)
                .Select(s => new { s.Id, s.Title }).ToListAsync(ct))
            .Select(s => (s.Id, s.Title))
            .ToList();

    public async Task<IReadOnlyList<(string SourceId, string SourceSeriesId, Guid LibraryId)>> ResolveLibraryLinksAsync(
        IReadOnlyCollection<(string SourceId, string SourceSeriesId)> refs, CancellationToken ct = default)
    {
        if (refs.Count == 0) return [];

        // EF can't translate a set of (source, series) pairs, so pre-filter on the two id sets in SQL,
        // then narrow to the exact pairs in memory. Both sets are small (one browse page).
        var sourceIds = refs.Select(r => r.SourceId).Distinct().ToList();
        var seriesIds = refs.Select(r => r.SourceSeriesId).Distinct().ToList();

        var links = await db.Series
            .SelectMany(s => s.SourceLinks, (s, l) => new { l.SourceId, l.SourceSeriesId, LibraryId = s.Id })
            .Where(x => sourceIds.Contains(x.SourceId) && seriesIds.Contains(x.SourceSeriesId))
            .ToListAsync(ct);

        var wanted = refs.ToHashSet();
        return links
            .Where(x => wanted.Contains((x.SourceId, x.SourceSeriesId)))
            .Select(x => (x.SourceId, x.SourceSeriesId, x.LibraryId))
            .ToList();
    }

    /// <summary>The cover's absolute path, or null if none is cached. Resolution lives here rather than in
    /// the endpoint because a stored cover path is relative to its own library's root — the caller would
    /// otherwise need the series' kind just to turn the string into a file.</summary>
    public async Task<string?> GetCoverFileAsync(Guid seriesId, CancellationToken ct = default)
    {
        var row = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => new { s.Kind, s.CoverPath })
            .FirstOrDefaultAsync(ct);

        return row?.CoverPath is null ? null : paths.Absolute(row.Kind, row.CoverPath);
    }

    public Task<Series?> GetSeriesAsync(Guid seriesId, CancellationToken ct = default) =>
        db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Tags)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .Include(s => s.Chapters).ThenInclude(c => c.Releases)
            .Include(s => s.Chapters).ThenInclude(c => c.ActiveRelease)
            .Include(s => s.Chapters).ThenInclude(c => c.ArtifactLinks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct);

    public async Task<IReadOnlyDictionary<Guid, ReadingProgress>> GetProgressAsync(
        Guid userId, Guid seriesId, CancellationToken ct = default) =>
        await db.ReadingProgress
            .Where(p => p.UserId == userId && p.Chapter.SeriesId == seriesId)
            .ToDictionaryAsync(p => p.ChapterId, ct);

    public async Task SetPreferredGroupsAsync(
        Guid seriesId, IReadOnlyList<string> groups, CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");
        series.PreferredGroups = groups.ToList();
        await db.SaveChangesAsync(ct);
    }

    public async Task SetPolicyAsync(
        Guid seriesId, int? gracePeriodDays, bool autoDownload, IReadOnlyList<string> languages,
        CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");
        series.GracePeriodDays = gracePeriodDays;
        series.AutoDownload = autoDownload;
        series.Languages = languages.ToList();
        await db.SaveChangesAsync(ct);
    }

    public Task<Follow?> GetFollowAsync(Guid userId, Guid seriesId, CancellationToken ct = default) =>
        db.Follows.FirstOrDefaultAsync(f => f.UserId == userId && f.SeriesId == seriesId, ct);

    public async Task<IReadOnlySet<Guid>> GetFollowedSeriesIdsAsync(
        Guid userId, IReadOnlyCollection<Guid> seriesIds, CancellationToken ct = default)
    {
        if (seriesIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var followed = await db.Follows
            .Where(f => f.UserId == userId && seriesIds.Contains(f.SeriesId))
            .Select(f => f.SeriesId)
            .ToListAsync(ct);

        return followed.ToHashSet();
    }

    public async Task<Follow> FollowAsync(
        Guid userId, Guid seriesId, IReadOnlyList<string> languages, bool autoDownload, CancellationToken ct = default)
    {
        var follow = await GetFollowAsync(userId, seriesId, ct);
        if (follow is null)
        {
            follow = new Follow { UserId = userId, SeriesId = seriesId };
            db.Follows.Add(follow);
        }

        follow.Languages = languages.ToList();
        follow.AutoDownload = autoDownload;
        await db.SaveChangesAsync(ct);
        return follow;
    }

    public async Task UnfollowAsync(Guid userId, Guid seriesId, CancellationToken ct = default)
    {
        var follow = await GetFollowAsync(userId, seriesId, ct);
        if (follow is not null)
        {
            db.Follows.Remove(follow);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<RecentDownloadItem>> GetRecentDownloadsAsync(
        MediaKind? kind, int limit, CancellationToken ct = default)
    {
        // Over-fetch before dedup-by-series — a few series with several recent downloads shouldn't
        // starve the rail of the requested number of distinct series. Real DB-level ordering now
        // that AppDbContext converts DateTimeOffset to a SQL-sortable UTC DateTime on SQLite.
        var fetch = Math.Max(limit * 5, 100);

        var artifacts = await db.Artifacts
            .Where(a => (kind == null || a.Series.Kind == kind) && a.Status == ArtifactStatus.Complete)
            .OrderByDescending(a => a.CreatedAt)
            .Take(fetch)
            .Select(a => new
            {
                a.SeriesId,
                a.CreatedAt,
                SeriesTitle = a.Series.Title,
                a.Series.CoverPath,
                Chapter = a.ChapterLinks
                    .Where(l => l.Chapter.ActiveArtifactId == a.Id)
                    .OrderBy(l => l.Order)
                    .Select(l => new { l.Chapter.Id, l.Chapter.Number, l.Chapter.Volume })
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var items = new List<RecentDownloadItem>();
        var seen = new HashSet<Guid>();
        foreach (var a in artifacts)
        {
            if (a.Chapter is null || !seen.Add(a.SeriesId))
            {
                continue;
            }

            items.Add(new RecentDownloadItem(
                a.SeriesId, a.SeriesTitle, a.CoverPath, a.Chapter.Id, a.Chapter.Number, a.Chapter.Volume, a.CreatedAt));

            if (items.Count >= limit)
            {
                break;
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<RecentlyUpdatedItem>> GetRecentlyUpdatedAsync(
        MediaKind? kind, int limit, CancellationToken ct = default)
    {
        // Over-fetch before dedup-by-series, same rationale as GetRecentDownloadsAsync.
        var fetch = Math.Max(limit * 5, 100);

        var releases = await db.ChapterReleases
            .Where(r => kind == null || r.Chapter.Series.Kind == kind)
            .OrderByDescending(r => r.DiscoveredAt)
            .Take(fetch)
            .Select(r => new
            {
                r.DiscoveredAt,
                SeriesId = r.Chapter.SeriesId,
                SeriesTitle = r.Chapter.Series.Title,
                r.Chapter.Series.CoverPath,
                ChapterId = r.Chapter.ActiveArtifactId != null ? r.Chapter.Id : (Guid?)null,
                r.Chapter.Number,
                r.Chapter.Volume,
            })
            .ToListAsync(ct);

        var items = new List<RecentlyUpdatedItem>();
        var seen = new HashSet<Guid>();
        foreach (var r in releases)
        {
            if (!seen.Add(r.SeriesId))
            {
                continue;
            }

            items.Add(new RecentlyUpdatedItem(
                r.SeriesId, r.SeriesTitle, r.CoverPath, r.ChapterId, r.Number, r.Volume, r.DiscoveredAt));

            if (items.Count >= limit)
            {
                break;
            }
        }

        return items;
    }

    public async Task DeleteSeriesAsync(Guid seriesId, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.Artifacts)
            .Include(s => s.Chapters).ThenInclude(c => c.Releases)
            .Include(s => s.Chapters).ThenInclude(c => c.ArtifactLinks)
            .Include(s => s.SourceLinks)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        // Collect the actual stored paths before anything is deleted — reconstructing them from the
        // series title would be wrong if the title's changed since an artifact was written (e.g. a
        // metadata refresh renaming the series after its files were already on disk).
        var artifactPaths = series.Artifacts
            .Select(a => (Path: paths.Absolute(series.Kind, a.Path), a.Format))
            .ToList();
        var coverPath = series.CoverPath is null ? null : paths.Absolute(series.Kind, series.CoverPath);

        await ClearActivePointersAsync(series.Chapters, ct);

        db.Series.Remove(series);
        await db.SaveChangesAsync(ct);

        foreach (var (path, format) in artifactPaths)
        {
            DeleteArtifactFile(path, format);
        }

        if (coverPath is not null && File.Exists(coverPath))
        {
            TryDelete(() => File.Delete(coverPath));
        }
    }

    public async Task DeleteChapterAsync(Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Releases)
            // Series is included for its Kind: an artifact's stored path is relative to its own library's
            // root, so it can't be resolved without knowing which library the chapter belongs to.
            .Include(c => c.Series)
            .Include(c => c.ArtifactLinks).ThenInclude(l => l.Artifact).ThenInclude(a => a.ChapterLinks)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct)
            ?? throw new InvalidOperationException("Chapter not found.");

        // An artifact this chapter is the sole remaining link for becomes orphaned once the chapter's
        // gone — delete it too (DB row + on-disk file). One still shared with other chapters (a multi-
        // chapter volume file) is left in place; only this chapter's link to it is forgotten.
        var artifactsToDelete = chapter.ArtifactLinks
            .Select(l => l.Artifact)
            .Where(a => a.ChapterLinks.All(l => l.ChapterId == chapterId))
            .Distinct()
            .ToList();
        var artifactPaths = artifactsToDelete
            .Select(a => (Path: paths.Absolute(chapter.Series.Kind, a.Path), a.Format))
            .ToList();

        await ClearActivePointersAsync([chapter], ct);

        // ArtifactChapter's Chapter-side FK is NoAction (see ArtifactChapterConfiguration) — removing
        // it explicitly is required regardless of whether the artifact itself is also being deleted.
        db.ArtifactChapters.RemoveRange(chapter.ArtifactLinks);
        db.Artifacts.RemoveRange(artifactsToDelete);
        db.Chapters.Remove(chapter);
        await db.SaveChangesAsync(ct);

        foreach (var (path, format) in artifactPaths)
        {
            DeleteArtifactFile(path, format);
        }
    }

    public async Task UpdateChapterAsync(
        Guid chapterId, string? number, string? volume, string? title, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.ActiveRelease)
            .Include(c => c.Releases)
            .Include(c => c.Series)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct)
            ?? throw new InvalidOperationException("Chapter not found.");

        var activeRelease = chapter.ActiveRelease ?? chapter.Releases.FirstOrDefault(r => r.Id == chapter.ActiveReleaseId);
        if (activeRelease?.SourceId != LocalSourceConstants.SourceId)
        {
            throw new InvalidOperationException("Only manually-imported chapters can be edited.");
        }

        var (sort, rawKey) = ChapterNumber.Normalize(number, volume, title);
        var key = ChapterNumber.QualifyKey(chapter.Series.SortMode, rawKey, volume);

        var collision = await db.Chapters
            .Where(c => c.SeriesId == chapter.SeriesId && c.Language == chapter.Language && c.Id != chapterId)
            .FirstOrDefaultAsync(c => c.NumberKey == key, ct);
        if (collision is not null)
        {
            throw new InvalidOperationException(
                $"That number collides with chapter {collision.Number ?? collision.Volume ?? collision.Title ?? "?"}.");
        }

        chapter.Number = number;
        chapter.Volume = volume;
        chapter.Title = title;
        chapter.NumberSort = sort;
        chapter.NumberKey = key;
        chapter.VolumeSort = ChapterNumber.VolumeSort(volume);

        await db.SaveChangesAsync(ct);
    }

    public async Task SetChapterSortModeAsync(Guid seriesId, ChapterSortMode mode, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.Chapters)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        if (series.SortMode == mode) return;

        var recomputed = series.Chapters
            .Select(c => (Chapter: c, Key: ChapterNumber.QualifyKey(mode, ChapterNumber.Normalize(c.Number, c.Volume, c.Title).Key, c.Volume)))
            .ToList();

        var collision = recomputed
            .GroupBy(x => (x.Chapter.Language, x.Key))
            .FirstOrDefault(g => g.Count() > 1);
        if (collision is not null)
        {
            var names = string.Join(", ", collision.Select(x => x.Chapter.Number ?? x.Chapter.Volume ?? x.Chapter.Title ?? "?"));
            throw new InvalidOperationException(
                $"Switching sort mode would merge these chapters onto the same number: {names}. " +
                "Give them distinct numbers/volumes first.");
        }

        // Two-phase save: a chapter's new qualified key can transiently collide with another chapter's
        // still-old key mid-transaction (SQLite checks the unique index per-statement, not at commit),
        // even though the final set of keys is already known to be collision-free above — Normalize's
        // non-numeric fallback branches embed arbitrary user text, so "qualified keys are structurally
        // distinguishable from unqualified ones" isn't a safe assumption to skip this on. A per-row
        // sentinel derived from the chapter's own id can't collide with anything.
        foreach (var (chapter, _) in recomputed)
        {
            chapter.NumberKey = $"__pending__{chapter.Id:N}";
        }

        await db.SaveChangesAsync(ct);

        foreach (var (chapter, key) in recomputed)
        {
            chapter.NumberKey = key;
            chapter.VolumeSort = ChapterNumber.VolumeSort(chapter.Volume);
        }

        series.SortMode = mode;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateSeriesMetadataAsync(
        Guid seriesId, string title, int? year, string? description, CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        series.Title = title.Trim();
        series.Year = year;
        series.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        series.LockedFields |= SeriesLockedFields.Title | SeriesLockedFields.Year | SeriesLockedFields.Description;
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlockMetadataAsync(Guid seriesId, CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        series.LockedFields &= ~(SeriesLockedFields.Title | SeriesLockedFields.Year | SeriesLockedFields.Description);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> SetCustomCoverAsync(Guid seriesId, Stream image, CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        if (!await coverCache.SetCustomCoverAsync(series, image, ct))
        {
            return false;
        }

        series.LockedFields |= SeriesLockedFields.Cover;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task UnlockCoverAsync(Guid seriesId, CancellationToken ct = default)
    {
        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        series.LockedFields &= ~SeriesLockedFields.Cover;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Nulls each chapter's ActiveArtifactId/ActiveReleaseId and saves before the chapter (and
    /// its releases/artifacts) are removed. Without this, EF sees a circular dependency — Chapter's
    /// ActiveReleaseId FK points at a ChapterRelease that's simultaneously being cascade-deleted as
    /// that same chapter's child — and refuses to order the deletes (mirrors the identical two-phase
    /// save LocalImportService's <em>insert</em> path already needs, for the same underlying cycle).</summary>
    private async Task ClearActivePointersAsync(IEnumerable<Chapter> chapters, CancellationToken ct)
    {
        var any = false;
        foreach (var chapter in chapters)
        {
            chapter.ActiveArtifactId = null;
            chapter.ActiveReleaseId = null;
            any = true;
        }

        if (any)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static void DeleteArtifactFile(string path, StorageFormat format) => TryDelete(() =>
    {
        if (format == StorageFormat.Cbz)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    });

    /// <summary>Best-effort on-disk cleanup — the DB rows are already gone by the time this runs, so a
    /// filesystem hiccup here shouldn't surface as a failed delete (the user can clean up manually;
    /// the library itself is already consistent).</summary>
    private static void TryDelete(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            /* best-effort */
        }
    }

    private static async Task<List<SourceChapter>> FetchAllChaptersAsync(
        IChapterSource source, string sourceSeriesId, CancellationToken ct)
    {
        var all = new List<SourceChapter>();
        var offset = 0;

        for (var page = 0; page < MaxFeedPages; page++)
        {
            var result = await source.GetChaptersAsync(
                sourceSeriesId,
                new ChapterQuery { Limit = FeedPageSize, Offset = offset, IncludeExternal = true },
                ct);

            all.AddRange(result.Items);
            offset += FeedPageSize;

            if (result.Items.Count < FeedPageSize || offset >= result.Total)
            {
                break;
            }
        }

        return all;
    }

    private Task TryCacheCoverAsync(Series series, SourceSeries source, CancellationToken ct) =>
        coverCache.TryCacheAsync(series, source.CoverUrl, ct);

}
