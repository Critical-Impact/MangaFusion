namespace MangaFusion.Domain.Library;

/// <summary>Links a <see cref="Series"/> to its identity on a source. Enables multi-source metadata
/// and the future local/manual source (<c>SourceId = "local"</c>).</summary>
public class SeriesSourceLink
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    public string SourceId { get; set; } = default!;
    public string SourceSeriesId { get; set; } = default!;

    /// <summary>The source treated as the metadata authority for this series.</summary>
    public bool IsMetadataPrimary { get; set; }
}
