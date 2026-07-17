namespace MangaFusion.Domain.Library;

/// <summary>A canonical tag (genre, theme, format, or a locally-created one), shared across every
/// series that carries it — a real many-to-many association rather than duplicated strings.</summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = default!;

    /// <summary>Tag rows are shared across series, so they carry their own kind rather than inheriting
    /// one — it keeps comic facets (publisher, character) out of the manga browse filters and vice versa.
    /// A name that exists in both kinds is two rows, which is correct: manga "Horror" (a MangaDex genre)
    /// and a comic "Horror" (a ComicVine concept) have different provenance and different meaning.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    /// <summary>Manga: "genre" | "theme" | "format" | "content" (a source's own grouping) | "other"
    /// (locally created, ungrouped). Comics: "publisher" | "character" | "team" | "story-arc" | "concept".</summary>
    public string Group { get; set; } = "other";

    /// <summary>Provenance for tags synced from a source's registry (e.g. "mangadex"); null for
    /// tags created locally (manual imports, or ad hoc during a local series' tagging).</summary>
    public string? SourceId { get; set; }
    public string? SourceTagId { get; set; }

    public List<Series> Series { get; set; } = [];
}
