using MangaFusion.Application.Downloads;
using MangaFusion.Application.Tasks;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Tasks;

/// <summary>Merges the Downloads table (source of truth for download tasks — rich pages/error detail)
/// with the queue engine's scan jobs (which have no domain row) into one admin feed.</summary>
public sealed class TaskFeedService(
    AppDbContext db,
    IBackgroundTaskQuery query,
    IDownloadService downloads) : ITaskFeedService
{
    public async Task<TaskFeed> GetFeedAsync(int limit, CancellationToken ct = default)
    {
        var titles = await db.Series.Select(s => new { s.Id, s.Title }).ToDictionaryAsync(s => s.Id, s => s.Title, ct);

        // Downloads: rich rows straight from the table (small).
        var downloadRows = await db.Downloads.ToListAsync(ct);
        var items = downloadRows.Select(d => ToDownloadItem(d, titles)).ToList();

        // Scans: only exist as queue jobs. Download-kind jobs are skipped — the table already covers them.
        var jobs = await query.GetJobsAsync(limit, ct);
        items.AddRange(jobs
            .Where(j => j.Kind is TaskKind.SeriesScan or TaskKind.LibraryScan)
            .Select(j => ToScanItem(j, titles)));

        var ordered = items
            .OrderByDescending(i => i.FinishedAt ?? i.StartedAt ?? i.CreatedAt ?? DateTimeOffset.MinValue)
            .Take(limit)
            .ToList();

        return new TaskFeed(await query.GetStatsAsync(ct), ordered);
    }

    public async Task<Guid> RetryDownloadAsync(Guid downloadId, CancellationToken ct = default)
    {
        var download = await db.Downloads.FirstOrDefaultAsync(d => d.Id == downloadId, ct)
            ?? throw new InvalidOperationException("Download not found.");

        if (download.Status != DownloadStatus.Failed)
        {
            throw new InvalidOperationException("Only failed downloads can be retried.");
        }

        if (download.ChapterId is null || download.ReleaseId is null)
        {
            throw new InvalidOperationException("This download has no chapter/release to retry.");
        }

        return await downloads.QueueChapterDownloadAsync(download.ChapterId.Value, download.ReleaseId, ct);
    }

    private static TaskFeedItem ToDownloadItem(Download d, IReadOnlyDictionary<Guid, string> titles)
    {
        var title = titles.GetValueOrDefault(d.SeriesId, "Unknown series");
        var target = string.IsNullOrWhiteSpace(d.Description) ? title : $"{title} · {d.Description}";
        return new TaskFeedItem(
            d.Id.ToString(),
            "download",
            target,
            d.SeriesId,
            d.Status switch
            {
                DownloadStatus.Queued => "Queued",
                DownloadStatus.Running => "Running",
                DownloadStatus.Completed => "Succeeded",
                DownloadStatus.Failed => "Failed",
                DownloadStatus.Cancelled => "Cancelled",
                _ => d.Status.ToString(),
            },
            d.PagesDone,
            d.PagesTotal,
            d.Error,
            d.HangfireJobId,
            d.CreatedAt,
            d.Status is DownloadStatus.Running or DownloadStatus.Completed ? d.CreatedAt : null,
            d.CompletedAt);
    }

    private static TaskFeedItem ToScanItem(BackgroundJobInfo j, IReadOnlyDictionary<Guid, string> titles)
    {
        var target = j.Kind == TaskKind.LibraryScan
            ? "All monitored series"
            : j.SeriesId is { } sid ? titles.GetValueOrDefault(sid, "Unknown series") : "Series scan";

        return new TaskFeedItem(
            $"hangfire:{j.JobId}",
            j.Kind == TaskKind.LibraryScan ? "library-scan" : "series-scan",
            target,
            j.SeriesId,
            j.State.ToString(),
            null,
            null,
            j.Error,
            j.JobId,
            null,
            j.StartedAt,
            j.FinishedAt);
    }
}
