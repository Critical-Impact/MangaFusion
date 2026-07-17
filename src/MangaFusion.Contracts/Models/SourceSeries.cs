namespace MangaFusion.Contracts.Models;

/// <summary>A series as described by a source. Provider-neutral; the source maps its own schema
/// onto this. Identified by (<see cref="SourceId"/>, <see cref="SourceSeriesId"/>).</summary>
public sealed record SourceSeries
{
    public required string SourceId { get; init; }
    public required string SourceSeriesId { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<string> AltTitles { get; init; } = [];
    public string? Description { get; init; }

    /// <summary>Absolute URL to the cover image at the source. The web layer proxies this so the
    /// browser never hits the source's CDN directly.</summary>
    public string? CoverUrl { get; init; }

    public IReadOnlyList<string> Authors { get; init; } = [];
    public IReadOnlyList<string> Artists { get; init; } = [];

    /// <summary>The same people as <see cref="Authors"/>/<see cref="Artists"/>, but carrying the
    /// source's own author id so they can be resolved against locally-persisted <c>Author</c> entities
    /// on import and filtered on via <see cref="SearchQuery.AuthorIds"/>. Empty for sources that don't
    /// expose per-author ids (e.g. local/manual imports).</summary>
    public IReadOnlyList<SourceAuthorRef> AuthorRefs { get; init; } = [];
    public IReadOnlyList<SourceAuthorRef> ArtistRefs { get; init; } = [];

    /// <summary>Flat tag names, for display where the source's tag identity doesn't matter.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>The same tags as <see cref="Tags"/>, but carrying the source's own tag id + group so
    /// they can be resolved against locally-persisted <c>Tag</c> entities on import. Empty for sources
    /// that don't expose per-tag ids/groups.</summary>
    public IReadOnlyList<SourceTagRef> TagRefs { get; init; } = [];

    public ContentRating ContentRating { get; init; } = ContentRating.Unknown;
    public PublicationStatus Status { get; init; } = PublicationStatus.Unknown;
    public int? Year { get; init; }
    public string? OriginalLanguage { get; init; }
    public IReadOnlyList<string> AvailableTranslatedLanguages { get; init; } = [];

    /// <summary>The source's "last chapter" number when known (e.g. "150"); null if unknown. Not a
    /// true count — a rough indicator only. See <see cref="ChapterCount"/> for an actual count.</summary>
    public string? LastChapter { get; init; }

    /// <summary>How many chapters/issues the source says the series has, when it reports a real count
    /// (ComicVine's <c>count_of_issues</c>); null when the source doesn't know or only exposes a
    /// last-chapter number. Distinct from <see cref="LastChapter"/>, which is a *number*, not a count —
    /// a series starting at issue 0, or with .5 chapters, has more chapters than its last number.
    ///
    /// Used to sanity-check import matches: a candidate with fewer issues than the user has files almost
    /// certainly isn't the right series.</summary>
    public int? ChapterCount { get; init; }

    /// <summary>Canonical human-facing page for the series on the source's own site, for linking out.
    /// Null when the source doesn't publish one.</summary>
    public string? SiteUrl { get; init; }
}
