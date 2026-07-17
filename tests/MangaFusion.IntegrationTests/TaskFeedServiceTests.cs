using MangaFusion.Application.Downloads;
using MangaFusion.Application.Tasks;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Tasks;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.IntegrationTests;

public class TaskFeedServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-tasks-{Guid.NewGuid():N}.db");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private static readonly BackgroundStats Stats = new(1, 2, 3, 4, 5, 1);

    [Fact]
    public async Task Merges_download_rows_with_scan_jobs_and_skips_hangfire_downloads()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = new Series { Title = "My Series" };
        series.SourceLinks.Add(new SeriesSourceLink { SourceId = "fake", SourceSeriesId = "s1", IsMetadataPrimary = true });
        db.Series.Add(series);
        await db.SaveChangesAsync();

        db.Downloads.AddRange(
            new Download
            {
                SeriesId = series.Id, Kind = DownloadKind.SingleRelease, ChapterId = Guid.NewGuid(),
                ReleaseId = Guid.NewGuid(), Description = "Ch. 1", Status = DownloadStatus.Failed,
                Error = "boom", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            },
            new Download
            {
                SeriesId = series.Id, Kind = DownloadKind.SingleRelease, Description = "Ch. 2",
                Status = DownloadStatus.Completed, PagesDone = 12, PagesTotal = 12,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-3), CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            });
        await db.SaveChangesAsync();

        var jobs = new List<BackgroundJobInfo>
        {
            new("j1", TaskKind.SeriesScan, null, series.Id, TaskState.Succeeded, null, null, DateTimeOffset.UtcNow),
            new("j2", TaskKind.LibraryScan, null, null, TaskState.Running, null, DateTimeOffset.UtcNow, null),
            // A download-kind engine job — must be ignored (the Downloads table is the source of truth).
            new("j3", TaskKind.Download, Guid.NewGuid(), null, TaskState.Succeeded, null, null, DateTimeOffset.UtcNow),
        };

        var svc = new TaskFeedService(db, new FakeTaskQuery(jobs, Stats), new FakeDownloads());
        var feed = await svc.GetFeedAsync(100);

        Assert.Equal(Stats, feed.Stats);

        // Exactly two download items — both from the table, none from the hangfire download job.
        Assert.Equal(2, feed.Items.Count(i => i.Kind == "download"));

        var failed = feed.Items.Single(i => i.State == "Failed");
        Assert.Equal("download", failed.Kind);
        Assert.Contains("My Series", failed.Target);
        Assert.Contains("Ch. 1", failed.Target);
        Assert.Equal("boom", failed.Error);

        Assert.Contains(feed.Items, i => i.Kind == "series-scan" && i.Target == "My Series");
        Assert.Contains(feed.Items, i => i.Kind == "library-scan" && i.Target == "All monitored series");
    }

    [Fact]
    public async Task Retry_requeues_a_failed_download_only()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var chapterId = Guid.NewGuid();
        var releaseId = Guid.NewGuid();
        var failed = new Download
        {
            SeriesId = Guid.NewGuid(), Kind = DownloadKind.SingleRelease, ChapterId = chapterId,
            ReleaseId = releaseId, Status = DownloadStatus.Failed,
        };
        var completed = new Download { SeriesId = Guid.NewGuid(), Status = DownloadStatus.Completed };
        db.Downloads.AddRange(failed, completed);
        await db.SaveChangesAsync();

        var downloads = new FakeDownloads();
        var svc = new TaskFeedService(db, new FakeTaskQuery([], Stats), downloads);

        await svc.RetryDownloadAsync(failed.Id);
        Assert.Equal((chapterId, (Guid?)releaseId), Assert.Single(downloads.Queued));

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RetryDownloadAsync(completed.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RetryDownloadAsync(Guid.NewGuid()));
    }

    private sealed class FakeTaskQuery(IReadOnlyList<BackgroundJobInfo> jobs, BackgroundStats stats) : IBackgroundTaskQuery
    {
        public Task<IReadOnlyList<BackgroundJobInfo>> GetJobsAsync(int limit, CancellationToken ct = default) =>
            Task.FromResult(jobs);

        public Task<BackgroundStats> GetStatsAsync(CancellationToken ct = default) => Task.FromResult(stats);
    }

    private sealed class FakeDownloads : IDownloadService
    {
        public List<(Guid ChapterId, Guid? ReleaseId)> Queued { get; } = [];

        public Task<Guid> QueueChapterDownloadAsync(Guid chapterId, Guid? releaseId = null, CancellationToken ct = default)
        {
            Queued.Add((chapterId, releaseId));
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<int> QueueSeriesMissingAsync(Guid seriesId, IReadOnlyList<string> languages, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<Download>> GetRecentAsync(int limit = 50, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Download>>([]);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }
}
