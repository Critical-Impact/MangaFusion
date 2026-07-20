using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Notifications;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.IntegrationTests;

/// <summary>Both of these queries used to materialize an entire table and sort it in memory, because
/// SQLite's provider won't translate ORDER BY on a DateTimeOffset. AppDbContext now stores every
/// DateTimeOffset as a UTC DateTime precisely so it can, and these tests hold that open: EF throws rather
/// than silently falling back to client evaluation, so if the ORDER BY ever stops translating, these fail
/// instead of quietly regressing into loading the whole table again.</summary>
public class RecentQueryOrderingTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-recent-{Guid.NewGuid():N}.db");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    [Fact]
    public async Task Recent_downloads_are_newest_first_and_limited_in_sql()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = new Series { Title = "Berserk", Kind = MediaKind.Manga };
        db.Series.Add(series);

        // Deliberately inserted out of order, so a passing test can't be an artifact of insertion order.
        var t0 = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        foreach (var offset in new[] { 2, 0, 4, 1, 3 })
        {
            db.Downloads.Add(new Download
            {
                SeriesId = series.Id,
                MediaKind = MediaKind.Manga,
                Kind = DownloadKind.SingleRelease,
                Status = DownloadStatus.Completed,
                Description = $"Ch. {offset}",
                CreatedAt = t0.AddHours(offset),
            });
        }

        await db.SaveChangesAsync();

        var recent = await new DownloadService(db, new NoopJobClient()).GetRecentAsync(limit: 3);

        Assert.Equal(3, recent.Count);
        Assert.Equal(["Ch. 4", "Ch. 3", "Ch. 2"], recent.Select(d => d.Description).ToList());
    }

    [Fact]
    public async Task Notifications_are_newest_first()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // Notification.UserId is a real FK, so the users have to exist.
        var userId = NewUser(db, "reader@test.local");
        var otherUserId = NewUser(db, "other@test.local");

        var t0 = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        foreach (var offset in new[] { 1, 3, 0, 2 })
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Kind = MediaKind.Manga,
                Title = $"N{offset}",
                CreatedAt = t0.AddHours(offset),
            });
        }

        // Another user's notification must not leak in — and it's the newest, so a missing user filter
        // would put it first.
        db.Notifications.Add(new Notification
        {
            UserId = otherUserId, Kind = MediaKind.Manga, Title = "other", CreatedAt = t0.AddHours(9),
        });
        await db.SaveChangesAsync();

        var service = new NotificationService(db, new NullNotifier(), null!);
        var items = await service.GetForUserAsync(userId, MediaKind.Manga, unreadOnly: false);

        Assert.Equal(["N3", "N2", "N1", "N0"], items.Select(n => n.Title).ToList());
    }

    private static Guid NewUser(AppDbContext db, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(user);
        return user.Id;
    }

    private sealed class NoopJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString();
        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }

    private sealed class NullNotifier : ILibraryNotifier
    {
        public Task DownloadProgressAsync(
            Guid downloadId, Guid? chapterId, DownloadStatus status, int pagesDone, int pagesTotal,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task ImportCommitProgressAsync(
            Guid importSeriesId, string status, int itemsDone, int itemsTotal, int? pageDone, int? pageTotal,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MigrationCommitProgressAsync(
            Guid migrationSeriesId, string status, int itemsDone, int itemsTotal, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MigrationBatchCommitProgressAsync(
            Guid batchId, int seriesDone, int seriesTotal, CancellationToken ct = default) => Task.CompletedTask;

        public Task NotificationAsync(Guid userId, string title, string? body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
