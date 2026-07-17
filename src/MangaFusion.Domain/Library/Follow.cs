namespace MangaFusion.Domain.Library;

/// <summary>A user's subscription to a series (per user). Group preference is a shared series setting;
/// the follow carries the user's language filter and auto-download opt-in.</summary>
public class Follow
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    public List<string> Languages { get; set; } = [];
    public bool AutoDownload { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
