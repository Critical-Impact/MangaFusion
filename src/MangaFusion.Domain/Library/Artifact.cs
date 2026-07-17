namespace MangaFusion.Domain.Library;

/// <summary>A physical downloaded file (CBZ or folder) on disk. Usually covers one chapter, but may
/// span several (a volume, or a local/manual multi-chapter file) via <see cref="ChapterLinks"/>.</summary>
public class Artifact
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    public StorageFormat Format { get; set; }

    public ArtifactOrigin Origin { get; set; } = ArtifactOrigin.Download;

    /// <summary>Path relative to the configured library root.</summary>
    public string Path { get; set; } = default!;

    public long SizeBytes { get; set; }
    public string? Hash { get; set; }
    public ArtifactStatus Status { get; set; } = ArtifactStatus.Pending;
    public int PageCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ArtifactChapter> ChapterLinks { get; set; } = [];
}
