using Hangfire;
using MangaFusion.Application.Downloads;
using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Downloads;

public sealed class DownloadService(AppDbContext db, IBackgroundJobClient jobs) : IDownloadService
{
    public async Task<Guid> QueueChapterDownloadAsync(
        Guid chapterId, Guid? releaseId = null, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Releases)
            .Include(c => c.Series)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct)
            ?? throw new InvalidOperationException("Chapter not found.");

        // Explicit release, else the best per the series' group preference.
        var release = releaseId is not null
            ? chapter.Releases.FirstOrDefault(r => r.Id == releaseId)
            : LibrarySelectionService.SelectBest(chapter.Releases, chapter.Series.PreferredGroups);

        if (release is null)
        {
            throw new InvalidOperationException("No downloadable release for this chapter.");
        }

        if (release.IsExternal)
        {
            throw new InvalidOperationException("This release is hosted externally and cannot be downloaded.");
        }

        var (downloadId, _) = await EnqueueAsync(chapter, release, chapter.Series.Kind, ct);
        return downloadId;
    }

    public async Task<int> QueueSeriesMissingAsync(
        Guid seriesId, IReadOnlyList<string> languages, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.Chapters).ThenInclude(c => c.Releases)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");

        var wantLanguages = languages.Count == 0 ? null : new HashSet<string>(languages, StringComparer.OrdinalIgnoreCase);
        var queued = 0;

        foreach (var chapter in series.Chapters)
        {
            if (chapter.ActiveArtifactId is not null)
            {
                continue; // already downloaded
            }

            if (wantLanguages is not null && !wantLanguages.Contains(chapter.Language))
            {
                continue;
            }

            var best = LibrarySelectionService.SelectBest(chapter.Releases, series.PreferredGroups);
            if (best is null)
            {
                continue; // only external / no releases
            }

            // Count only what this call actually queued — a chapter already in flight (e.g. the user hit
            // "download missing" twice) is skipped, not double-counted.
            var (_, created) = await EnqueueAsync(chapter, best, series.Kind, ct);
            if (created)
            {
                queued++;
            }
        }

        return queued;
    }

    /// <summary>Ordered and limited in SQL. This used to materialize the whole table and sort in memory,
    /// because SQLite's provider refuses to translate ORDER BY on a DateTimeOffset — but AppDbContext now
    /// stores every DateTimeOffset as a UTC DateTime for exactly that reason, so the ORDER BY translates.
    /// It matters here: nothing prunes Downloads (a row per chapter ever downloaded) and the downloads view
    /// polls this, so "the table is small" stopped being true after the first large backfill.</summary>
    public async Task<IReadOnlyList<Download>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
        await db.Downloads
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(ct);

    // Kind is passed in rather than read off chapter.Series: on the QueueSeriesMissingAsync path the
    // chapter comes from series.Chapters and its Series back-reference isn't guaranteed to be loaded.
    private async Task<(Guid Id, bool Created)> EnqueueAsync(
        Chapter chapter, ChapterRelease release, MediaKind kind, CancellationToken ct)
    {
        // Queueing a chapter that's already in flight is a no-op that returns the download already
        // running it. Two jobs for one chapter would race on the same artifact write and both retarget
        // Chapter.ActiveArtifactId, orphaning whichever row lost — so the guard lives here, on the path
        // every caller shares, rather than in each of them. (MonitorService additionally skips pending
        // chapters when *planning*, which keeps them out of the decision list; this is the backstop for
        // the API callers, where a double-click is the common case.)
        var inFlight = await db.Downloads
            .Where(d => d.ChapterId == chapter.Id
                        && (d.Status == DownloadStatus.Queued || d.Status == DownloadStatus.Running))
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(ct);
        if (inFlight is { } existing)
        {
            return (existing, false);
        }

        var download = new Download
        {
            SeriesId = chapter.SeriesId,
            MediaKind = kind,
            Kind = DownloadKind.SingleRelease,
            ReleaseId = release.Id,
            ChapterId = chapter.Id,
            Status = DownloadStatus.Queued,
            Description = (chapter.Number is null ? "Oneshot" : $"Ch. {chapter.Number}")
                          + (release.GroupKey is null ? "" : $" [{release.GroupKey}]"),
        };
        db.Downloads.Add(download);
        await db.SaveChangesAsync(ct);

        download.HangfireJobId = jobs.Enqueue<DownloadOrchestrator>(o => o.RunAsync(download.Id, CancellationToken.None));
        await db.SaveChangesAsync(ct);

        return (download.Id, true);
    }
}
