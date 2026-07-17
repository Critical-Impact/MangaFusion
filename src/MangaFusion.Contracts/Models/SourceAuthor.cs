namespace MangaFusion.Contracts.Models;

/// <summary>An author/artist as attached to one specific series, carrying enough identity to resolve
/// it against the source's author on import and to filter a search by <see cref="SearchQuery.AuthorIds"/>.</summary>
public sealed record SourceAuthorRef(string Id, string Name);
