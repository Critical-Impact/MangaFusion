using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Library;

/// <summary>Browse query for <see cref="ILibraryService.QueryLibraryAsync"/>.</summary>
public sealed record LibraryQuery(
    /// <summary>Which library to query. Always set — a query is never across both kinds.</summary>
    MediaKind Kind,
    string? Search,
    /// <summary>Tag filters, one entry per facet. Each facet is an OR match within itself (a series
    /// qualifies if it carries at least one of that facet's tags); facets combine with AND.
    ///
    /// Deliberately a list of id-sets rather than named genre/theme fields: the facets differ by kind —
    /// manga filters on genre/theme, comics on publisher/character/concept (ComicVine has no genre
    /// vocabulary at all) — and filtering only ever needs the ids, never the group they came from.</summary>
    IReadOnlyList<IReadOnlyList<Guid>> TagFacets,
    ContentRating? Rating,
    string Sort,
    string Order,
    int Limit,
    int Offset,
    /// <summary>Native author identity to filter by — the source's own author id (e.g. the MangaDex
    /// UUID), paired with <see cref="AuthorSourceId"/> identifying which source it came from
    /// ("local" for name-only authors from manual imports). Both null = no author filter.</summary>
    string? AuthorSourceId = null,
    string? AuthorNativeId = null,
    /// <summary>Restricts to series carrying a <c>SeriesSourceLink</c> for this source id (e.g.
    /// "mangadex", "mangaupdates", "local"). Null = no source filter.</summary>
    string? SourceId = null);

/// <summary>Lean per-series projection for the library list — deliberately excludes chapters/releases,
/// which are expensive to load for every row in a paged list.</summary>
public sealed record LibraryListItem(
    Guid Id,
    string Title,
    string? CoverPath,
    IReadOnlyList<string> Tags,
    int? Year,
    DateTimeOffset AddedAt,
    int ChapterCount,
    /// <summary>Every source this series carries a <c>SeriesSourceLink</c> for (e.g. "mangadex",
    /// "mangaupdates", "local") — feeds the library's source-provenance badge/filter.</summary>
    IReadOnlyList<string> Sources);

public sealed record LibraryPage(IReadOnlyList<LibraryListItem> Items, int Total);

/// <summary>A tag as exposed to the browse/filter UI and the local-import tag picker. <see cref="SourceId"/>
/// and <see cref="SourceTagId"/> are the tag's provenance (e.g. "mangadex" and its tag UUID) — null for
/// locally-created tags — used to link out to the source's own genre/theme search.</summary>
public sealed record TagInfo(Guid Id, string Name, string Group, string? SourceId = null, string? SourceTagId = null);
