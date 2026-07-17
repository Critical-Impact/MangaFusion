namespace MangaFusion.Domain.Library;

/// <summary>Membership of a <see cref="Series"/> in a <see cref="Collection"/>. An explicit join (not
/// an implicit many-to-many) so a manual ordering can be stored on <see cref="Position"/>.</summary>
public class CollectionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CollectionId { get; set; }

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    /// <summary>Zero-based order within the collection, used when the collection's sort is
    /// <see cref="MemberSort.Manual"/>. Ignored (but retained) under the preset sorts.</summary>
    public int Position { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
