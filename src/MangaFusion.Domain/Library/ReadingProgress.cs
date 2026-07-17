namespace MangaFusion.Domain.Library;

/// <summary>Per-user reading position for a logical chapter. Keyed on the logical <see cref="Chapter"/>
/// so it survives a group replacement (page index is reset on replace, but the read flag is kept).</summary>
public class ReadingProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = default!;

    public int PageIndex { get; set; }
    public bool Completed { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
