namespace MangaFusion.Domain.Library;

/// <summary>Per-user reading-list membership for a series. A series shows in "Continue reading" if the
/// user has read any of its chapters <em>or</em> has an entry here with <see cref="Dismissed"/> false;
/// a dismissed entry hides it from the rail regardless of progress.</summary>
public class SeriesReadingEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    /// <summary>Hidden from the reading rail. Set when the user dismisses the series.</summary>
    public bool Dismissed { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
