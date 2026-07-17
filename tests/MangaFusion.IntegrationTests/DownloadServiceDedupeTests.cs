using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.IntegrationTests;

/// <summary>Queueing the same chapter twice must not produce two downloads. Two jobs for one chapter race
/// on the same artifact write and both retarget Chapter.ActiveArtifactId, orphaning whichever row loses —
/// and a double-clicked download button is the ordinary way to get there.</summary>
public class DownloadServiceDedupeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-dl-dedupe-{Guid.NewGuid():N}.db");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private async Task<(Guid ChapterId, Guid SeriesId)> SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        var series = new Series { Title = "Berserk", Kind = MediaKind.Manga };
        var chapter = new Chapter
        {
            Series = series, Language = "en", Number = "1", NumberKey = "1", NumberSort = 1m,
        };
        chapter.Releases.Add(new ChapterRelease
        {
            SourceId = "mangadex",
            SourceChapterId = Guid.NewGuid().ToString(),
            ScanlationGroups = ["A"],
            GroupKey = "A",
            PublishedAt = DateTimeOffset.UtcNow,
            PageCount = 10,
        });

        db.Series.Add(series);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();
        return (chapter.Id, series.Id);
    }

    [Fact]
    public async Task Queueing_the_same_chapter_twice_reuses_the_in_flight_download()
    {
        await using var db = NewContext();
        var (chapterId, _) = await SeedAsync(db);
        var jobs = new RecordingJobClient();
        var service = new DownloadService(db, jobs);

        var first = await service.QueueChapterDownloadAsync(chapterId);
        var second = await service.QueueChapterDownloadAsync(chapterId);

        Assert.Equal(first, second); // idempotent: the caller gets the download that's already running it
        Assert.Equal(1, await db.Downloads.CountAsync());
        Assert.Equal(1, jobs.Created); // and only one background job was ever enqueued
    }

    /// <summary>A chapter that already finished downloading is a different case: it's no longer in flight, so
    /// nothing is deduped against, and a deliberate re-download is still allowed.</summary>
    [Fact]
    public async Task A_completed_download_does_not_block_re_queueing()
    {
        await using var db = NewContext();
        var (chapterId, _) = await SeedAsync(db);
        var service = new DownloadService(db, new RecordingJobClient());

        var first = await service.QueueChapterDownloadAsync(chapterId);
        var download = await db.Downloads.FirstAsync(d => d.Id == first);
        download.Status = DownloadStatus.Completed;
        await db.SaveChangesAsync();

        var second = await service.QueueChapterDownloadAsync(chapterId);

        Assert.NotEqual(first, second);
        Assert.Equal(2, await db.Downloads.CountAsync());
    }

    /// <summary>"Download missing" run twice must not queue everything a second time — and must report 0
    /// newly queued the second time, not re-count the chapters already in flight.</summary>
    [Fact]
    public async Task Queue_missing_run_twice_queues_each_chapter_once()
    {
        await using var db = NewContext();
        var (_, seriesId) = await SeedAsync(db);
        var service = new DownloadService(db, new RecordingJobClient());

        Assert.Equal(1, await service.QueueSeriesMissingAsync(seriesId, []));
        Assert.Equal(0, await service.QueueSeriesMissingAsync(seriesId, []));
        Assert.Equal(1, await db.Downloads.CountAsync());
    }

    private sealed class RecordingJobClient : IBackgroundJobClient
    {
        public int Created { get; private set; }

        public string Create(Job job, IState state)
        {
            Created++;
            return Guid.NewGuid().ToString();
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
