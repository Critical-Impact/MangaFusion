using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Notifications;

/// <summary>Creates and manages per-user in-app notifications. Notifications belong to one library —
/// the bell in comic mode shouldn't show a manga download failure — so every read and write is scoped
/// by <see cref="MediaKind"/>.</summary>
public interface INotificationService
{
    Task CreateAsync(
        Guid userId, MediaKind kind, string title, string? body, Guid? seriesId,
        NotificationSeverity severity = NotificationSeverity.Info, CancellationToken ct = default);

    /// <summary>Notifies every admin — for background failures (downloads, monitor scans) with no
    /// single owning user to notify instead.</summary>
    Task CreateForAdminsAsync(
        MediaKind kind, string title, string? body, Guid? seriesId, NotificationSeverity severity,
        CancellationToken ct = default);

    Task<IReadOnlyList<Notification>> GetForUserAsync(
        Guid userId, MediaKind kind, bool unreadOnly, CancellationToken ct = default);

    Task<int> UnreadCountAsync(Guid userId, MediaKind kind, CancellationToken ct = default);

    Task MarkReadAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken ct = default);

    Task MarkAllReadAsync(Guid userId, MediaKind kind, CancellationToken ct = default);
}
