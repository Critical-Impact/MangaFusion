namespace MangaFusion.Domain.Library;

/// <summary>A per-user in-app notification (e.g. new chapters found for a followed series).</summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Which library's notification bell this belongs to. Carried rather than inherited because
    /// <c>SeriesId</c> is nullable — a global notification has no series to take a kind from.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    public string Title { get; set; } = default!;
    public string? Body { get; set; }

    public Guid? SeriesId { get; set; }

    public NotificationSeverity Severity { get; set; } = NotificationSeverity.Info;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
