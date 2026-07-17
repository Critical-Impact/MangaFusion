namespace MangaFusion.Contracts.Models;

/// <summary>A filterable tag from a source (genre, theme, etc.). <see cref="Id"/> is passed back in
/// <see cref="SearchQuery.IncludedTags"/> to filter a search.</summary>
public sealed record SourceTag(string Id, string Name, string Group);

/// <summary>A tag as attached to one specific series, carrying enough identity to resolve it against
/// the source's tag registry on import.</summary>
public sealed record SourceTagRef(string Id, string Name, string Group);
