namespace MangaFusion.Domain.Library;

/// <summary>Canonical library entry for a series — a manga title or a comic volume. Metadata + shared
/// download policy. Downloads are shared across users, so the group-preference policy lives here (not
/// per-user).</summary>
public class Series
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which half of the library this belongs to. Chapters, artifacts, follows and progress all
    /// inherit this by navigation, so this is the single point of truth for the whole aggregate.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    public string Title { get; set; } = default!;
    public List<string> AltTitles { get; set; } = [];
    public string? Description { get; set; }

    /// <summary>Cached local cover path, relative to the library root (null until cached).</summary>
    public string? CoverPath { get; set; }

    public List<Author> Authors { get; set; } = [];
    public List<Author> Artists { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public ContentRating ContentRating { get; set; } = ContentRating.Unknown;
    public PublicationStatus Status { get; set; } = PublicationStatus.Unknown;
    public int? Year { get; set; }
    public string? OriginalLanguage { get; set; }

    // --- Shared download policy -----------------------------------------------------------------
    /// <summary>Ordered scanlation-group preference (highest first). Unlisted groups are fallback.</summary>
    public List<string> PreferredGroups { get; set; } = [];

    /// <summary>Grace window before falling back to a non-preferred group; null = global default.</summary>
    public int? GracePeriodDays { get; set; }

    /// <summary>Series-level auto-download override (unioned with per-user follow opt-ins).</summary>
    public bool AutoDownload { get; set; }

    /// <summary>Series-level auto-download languages (unioned with auto-download followers' languages).</summary>
    public List<string> Languages { get; set; } = [];

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When the monitor last scanned this series' source feed (null = never).</summary>
    public DateTimeOffset? LastScannedAt { get; set; }

    public List<SeriesSourceLink> SourceLinks { get; set; } = [];
    public List<Chapter> Chapters { get; set; } = [];
    public List<Artifact> Artifacts { get; set; } = [];
}
