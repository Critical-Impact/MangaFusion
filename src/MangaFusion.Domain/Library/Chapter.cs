namespace MangaFusion.Domain.Library;

/// <summary>A logical chapter — what a user reads and tracks. De-duplicated across scanlation groups
/// by <c>(SeriesId, Language, NumberKey)</c>. Its concrete downloadable variants are
/// <see cref="ChapterRelease"/>s; the file currently providing it is <see cref="ActiveArtifact"/>.</summary>
public class Chapter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public Series Series { get; set; } = default!;

    public string Language { get; set; } = default!;

    /// <summary>Display number, e.g. "10.5"; null for oneshots.</summary>
    public string? Number { get; set; }

    /// <summary>Sortable numeric form of <see cref="Number"/> (null when unparseable / oneshot).</summary>
    public decimal? NumberSort { get; set; }

    /// <summary>Normalized identity key within (series, language) used to collapse group variants.</summary>
    public string NumberKey { get; set; } = default!;

    public string? Volume { get; set; }

    /// <summary>Sortable numeric form of <see cref="Volume"/> (null when unparseable/absent). Only
    /// used for ordering when the series' <see cref="Series.SortMode"/> is
    /// <see cref="ChapterSortMode.VolumeThenChapter"/> — otherwise unused.</summary>
    public decimal? VolumeSort { get; set; }

    public string? Title { get; set; }

    /// <summary>The artifact currently providing this chapter for reading (null = not downloaded).</summary>
    public Guid? ActiveArtifactId { get; set; }
    public Artifact? ActiveArtifact { get; set; }

    /// <summary>The release the active artifact was produced from (null for local/manual).</summary>
    public Guid? ActiveReleaseId { get; set; }
    public ChapterRelease? ActiveRelease { get; set; }

    public List<ChapterRelease> Releases { get; set; } = [];
    public List<ArtifactChapter> ArtifactLinks { get; set; } = [];
}
