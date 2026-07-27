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

    /// <summary>Mirrors the owning <see cref="Series.Kind"/> (immutable once set). Denormalized onto the
    /// link so source-entry identity can be made unique <em>per kind</em> at the DB level — one source
    /// entry (e.g. a MangaUpdates id shared by a manga and its light-novel adaptation) may back one
    /// series per <see cref="MediaKind"/>. Always assign from the parent series at creation.</summary>
    public MediaKind Kind { get; set; }

    /// <summary>The source treated as the metadata authority for this series.</summary>
    public bool IsMetadataPrimary { get; set; }
}
