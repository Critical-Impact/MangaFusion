namespace MangaFusion.Domain.Library;

/// <summary>A canonical author/artist, shared across every series that credits them — a real
/// many-to-many association rather than duplicated strings. The same person can be an author on one
/// series and an artist on another (or both on the same one), so a single catalog entry backs both
/// <see cref="Series.Authors"/> and <see cref="Series.Artists"/>.</summary>
public class Author
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;

    /// <summary>Provenance for authors resolved from a source's relationship data (e.g. "mangadex");
    /// null for authors created locally (manual imports have no source author registry).</summary>
    public string? SourceId { get; set; }
    public string? SourceAuthorId { get; set; }

    public List<Series> AuthorOf { get; set; } = [];
    public List<Series> ArtistOf { get; set; } = [];
}
