using MangaFusion.Application.Notifications;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Notifications;

public sealed class NotificationService(
    AppDbContext db, ILibraryNotifier notifier, UserManager<ApplicationUser> users) : INotificationService
{
    public async Task CreateAsync(
        Guid userId, MediaKind kind, string title, string? body, Guid? seriesId,
        NotificationSeverity severity = NotificationSeverity.Info, CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification
        {
            UserId = userId,
            Kind = kind,
            Title = title,
            Body = body,
            SeriesId = seriesId,
            Severity = severity,
        });
        await db.SaveChangesAsync(ct);
        await notifier.NotificationAsync(userId, title, body, ct);
    }

    public async Task CreateForAdminsAsync(
        MediaKind kind, string title, string? body, Guid? seriesId, NotificationSeverity severity,
        CancellationToken ct = default)
    {
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        foreach (var admin in admins)
        {
            await CreateAsync(admin.Id, kind, title, body, seriesId, severity, ct);
        }
    }

    public async Task<IReadOnlyList<Notification>> GetForUserAsync(
        Guid userId, MediaKind kind, bool unreadOnly, CancellationToken ct = default)
    {
        var query = db.Notifications.Where(n => n.UserId == userId && n.Kind == kind);
        if (unreadOnly)
        {
            query = query.Where(n => n.ReadAt == null);
        }

        // Ordered/limited in SQL: AppDbContext stores DateTimeOffset as a UTC DateTime under SQLite, which
        // is what makes this translatable (it wasn't, hence the in-memory sort this replaces).
        return await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(ct);
    }

    public Task<int> UnreadCountAsync(Guid userId, MediaKind kind, CancellationToken ct = default) =>
        db.Notifications.CountAsync(n => n.UserId == userId && n.Kind == kind && n.ReadAt == null, ct);

    public async Task MarkReadAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Notifications
            .Where(n => n.UserId == userId && ids.Contains(n.Id) && n.ReadAt == null)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.ReadAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task MarkAllReadAsync(Guid userId, MediaKind kind, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await db.Notifications
            .Where(n => n.UserId == userId && n.Kind == kind && n.ReadAt == null)
            .ToListAsync(ct);
        foreach (var row in rows)
        {
            row.ReadAt = now;
        }

        await db.SaveChangesAsync(ct);
    }
}
