using MangaFusion.Application.Downloads;
using MangaFusion.Application.Notifications;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Monitoring;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MediaKind = MangaFusion.Contracts.Models.MediaKind;
using DomainKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.IntegrationTests;

public class MonitorServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-monitor-{Guid.NewGuid():N}.db");
    private readonly DateTimeOffset _t0 = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    private AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static Series NewSeries()
    {
        var series = new Series
        {
            Title = "Test",
            AutoDownload = true,
            Languages = ["en"],
            PreferredGroups = ["A"],
            GracePeriodDays = 7,
        };
        series.SourceLinks.Add(new SeriesSourceLink { SourceId = "fake", SourceSeriesId = "s1", IsMetadataPrimary = true });
        return series;
    }

    private static MonitorService BuildMonitor(
        AppDbContext db, ISource source, FakeDownloadService downloads, TimeProvider clock)
    {
        return new MonitorService(
            db,
            new SourceRegistry([source]),
            new ChapterImporter(db),
            downloads,
            new FakeNotificationService(),
            new SettingsService(db, new ConfigurationBuilder().Build()),
            new SeriesMetadataApplier(new AuthorResolver(db), new TagResolver(db)),
            clock,
            NullLogger<MonitorService>.Instance);
    }

    [Fact]
    public async Task Scan_imports_new_chapter_and_queues_preferred_download()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var source = new FakeChapterSource("fake", [Chapter("c1", "1", group: "A", publishedAt: _t0)]); // preferred
        var downloads = new FakeDownloadService();

        await BuildMonitor(db, source, downloads, new FakeClock(_t0)).ScanSeriesAsync(series.Id, default);

        Assert.Single(downloads.Queued);
        Assert.Equal(1, await db.Chapters.CountAsync());
        Assert.NotNull(series.LastScannedAt);
    }

    /// <summary>A series committed by the import wizard has a metadata-only source (MangaUpdates, and
    /// soon ComicVine) as its metadata-primary link. Following one used to make every recurring scan
    /// throw <c>SourceCapabilityException</c> and post an admin "Series scan failed" notification.</summary>
    [Fact]
    public async Task Scan_of_metadata_only_source_refreshes_metadata_without_throwing()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var source = new FakeMetadataOnlySource("fake", new SourceSeries
        {
            SourceId = "fake",
            SourceSeriesId = "s1",
            Title = "Refreshed Title",
        });
        var downloads = new FakeDownloadService();

        await BuildMonitor(db, source, downloads, new FakeClock(_t0)).ScanSeriesAsync(series.Id, default);

        Assert.Equal("Refreshed Title", series.Title);
        Assert.Equal(0, await db.Chapters.CountAsync());
        Assert.Empty(downloads.Queued);
        Assert.NotNull(series.LastScannedAt);
    }

    /// <summary>A source may serve a smaller page than it was asked for — ComicVine caps at 100 however
    /// large the requested limit. The feed loop must advance by what it actually received: advancing by the
    /// requested page size instead would leap over everything in between, silently importing only the first
    /// page of a long series and reporting success.</summary>
    [Fact]
    public async Task Scan_pages_a_full_feed_even_when_the_source_clamps_the_page_size()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        series.AutoDownload = false; // only the import path is under test here
        db.Series.Add(series);
        await db.SaveChangesAsync();

        // 250 chapters served 100 at a time, however many the caller asks for.
        var chapters = Enumerable.Range(1, 250)
            .Select(i => Chapter($"c{i}", i.ToString(), group: "A", publishedAt: _t0))
            .ToList();
        var source = new FakeClampedPageSource("fake", chapters, maxPageSize: 100);

        await BuildMonitor(db, source, new FakeDownloadService(), new FakeClock(_t0))
            .ScanSeriesAsync(series.Id, default);

        Assert.Equal(250, await db.Chapters.CountAsync());
    }

    /// <summary>ComicVine lists issues but cannot serve pages. Chapters must still import, but planning
    /// an auto-download for them would be guaranteed to fail in the orchestrator.</summary>
    [Fact]
    public async Task Scan_of_non_downloadable_chapter_source_imports_chapters_but_queues_nothing()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var source = new FakeNonDownloadableChapterSource("fake", [Chapter("c1", "1", group: "A", publishedAt: _t0)]);
        var downloads = new FakeDownloadService();

        await BuildMonitor(db, source, downloads, new FakeClock(_t0)).ScanSeriesAsync(series.Id, default);

        Assert.Equal(1, await db.Chapters.CountAsync());
        Assert.Empty(downloads.Queued);
    }

    [Fact]
    public async Task Grace_defers_non_preferred_until_window_elapses()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var source = new FakeChapterSource("fake", [Chapter("c1", "1", group: "B", publishedAt: _t0)]); // non-preferred

        // Within grace window -> deferred.
        var deferred = new FakeDownloadService();
        await BuildMonitor(db, source, deferred, new FakeClock(_t0)).ScanSeriesAsync(series.Id, default);
        Assert.Empty(deferred.Queued);

        // 8 days later, still only the non-preferred group -> now it downloads.
        var later = new FakeDownloadService();
        await BuildMonitor(db, source, later, new FakeClock(_t0.AddDays(8))).ScanSeriesAsync(series.Id, default);
        Assert.Single(later.Queued);
    }

    [Fact]
    public async Task Scan_refreshes_series_metadata_and_tags()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var source = new FakeMetadataAndChapterSource(
            "fake", [],
            new SourceSeries
            {
                SourceId = "fake",
                SourceSeriesId = "s1",
                Title = "Refreshed Title",
                TagRefs = [new SourceTagRef("t1", "Action", "genre")],
            });

        var monitor = new MonitorService(
            db, new SourceRegistry([source]), new ChapterImporter(db), new FakeDownloadService(),
            new FakeNotificationService(), new SettingsService(db, new ConfigurationBuilder().Build()),
            new SeriesMetadataApplier(new AuthorResolver(db), new TagResolver(db)),
            new FakeClock(_t0), NullLogger<MonitorService>.Instance);

        await monitor.ScanSeriesAsync(series.Id, default);

        var updated = await db.Series.Include(s => s.Tags).SingleAsync(s => s.Id == series.Id);
        Assert.Equal("Refreshed Title", updated.Title);
        Assert.Equal(["Action"], updated.Tags.Select(t => t.Name));
    }

    private static SourceChapter Chapter(string id, string number, string group, DateTimeOffset publishedAt) => new()
    {
        SourceId = "fake",
        SourceChapterId = id,
        Number = number,
        Language = "en",
        ScanlationGroups = [group],
        IsExternal = false,
        PublishedAt = publishedAt,
    };

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
    }

    private sealed class FakeClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    // Stands in for a MangaDex-like source: the monitor only plans auto-downloads for sources that can
    // actually serve pages, so a fake whose chapters are expected to be queued must declare Download.
    // GetPagesAsync is never reached — these tests assert the download is *queued*, not run.
    private sealed class FakeChapterSource(string id, IReadOnlyList<SourceChapter> chapters)
        : ISource, IChapterSource, IDownloadSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Chapters | SourceCapabilities.Download;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceChapter>> GetChaptersAsync(
            string sourceSeriesId, ChapterQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SourceChapter>(chapters, chapters.Count, 500, 0));

        public Task<SourcePageSet> GetPagesAsync(
            string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>MangaUpdates/ComicVine-shaped: metadata, no chapter feed.</summary>
    private sealed class FakeMetadataOnlySource(string id, SourceSeries metadata)
        : ISource, IMetadataSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            Task.FromResult<SourceSeries?>(metadata);

        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourceTag>>([]);
    }

    /// <summary>ComicVine-shaped: serves at most <paramref name="maxPageSize"/> items per call no matter what
    /// limit the caller asks for, while reporting the true total.</summary>
    private sealed class FakeClampedPageSource(
        string id, IReadOnlyList<SourceChapter> chapters, int maxPageSize)
        : ISource, IChapterSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Chapters;
        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Comic];

        public Task<PagedResult<SourceChapter>> GetChaptersAsync(
            string sourceSeriesId, ChapterQuery query, CancellationToken ct = default)
        {
            var take = Math.Min(query.Limit, maxPageSize);
            var page = chapters.Skip(query.Offset).Take(take).ToList();
            return Task.FromResult(new PagedResult<SourceChapter>(page, chapters.Count, take, query.Offset));
        }
    }

    /// <summary>ComicVine-shaped: lists issues, but has no pages to download.</summary>
    private sealed class FakeNonDownloadableChapterSource(string id, IReadOnlyList<SourceChapter> chapters)
        : ISource, IChapterSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Chapters;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceChapter>> GetChaptersAsync(
            string sourceSeriesId, ChapterQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SourceChapter>(chapters, chapters.Count, 500, 0));
    }

    private sealed class FakeMetadataAndChapterSource(string id, IReadOnlyList<SourceChapter> chapters, SourceSeries metadata)
        : ISource, IChapterSource, IMetadataSource, IDownloadSource
    {
        public string Id => id;
        public string DisplayName => id;

        public SourceCapabilities Capabilities =>
            SourceCapabilities.Chapters | SourceCapabilities.Metadata | SourceCapabilities.Download;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceChapter>> GetChaptersAsync(
            string sourceSeriesId, ChapterQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SourceChapter>(chapters, chapters.Count, 500, 0));

        public Task<SourcePageSet> GetPagesAsync(
            string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            Task.FromResult<SourceSeries?>(metadata);

        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeDownloadService : IDownloadService
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

    private sealed class FakeNotificationService : INotificationService
    {
        public Task CreateAsync(
            Guid userId, DomainKind kind, string title, string? body, Guid? seriesId,
            NotificationSeverity severity = NotificationSeverity.Info, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task CreateForAdminsAsync(
            DomainKind kind, string title, string? body, Guid? seriesId, NotificationSeverity severity,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Notification>> GetForUserAsync(
            Guid userId, DomainKind kind, bool unreadOnly, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Notification>>([]);

        public Task<int> UnreadCountAsync(Guid userId, DomainKind kind, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task MarkReadAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MarkAllReadAsync(Guid userId, DomainKind kind, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
