namespace MangaFusion.Domain.Library;

/// <summary>Join row recording that an <see cref="Artifact"/> contains a <see cref="Chapter"/>, and in
/// what order within the file (for multi-chapter artifacts).</summary>
public class ArtifactChapter
{
    public Guid ArtifactId { get; set; }
    public Artifact Artifact { get; set; } = default!;

    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = default!;

    public int Order { get; set; }

    /// <summary>Number of pages this chapter occupies within the artifact, starting at the cumulative
    /// offset of preceding links. Set at import time for multi-chapter files; null for single-chapter
    /// artifacts (which span the whole file).</summary>
    public int? PageCount { get; set; }
}
