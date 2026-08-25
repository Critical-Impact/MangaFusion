namespace MangaFusion.Contracts.Models;

/// <summary>An author/artist as attached to one specific series, carrying enough identity to resolve
/// it against the source's author on import and to filter a search by <see cref="SearchQuery.AuthorIds"/>.
/// <paramref name="Id"/> is null when the source has no author record for the credit. MangaUpdates
/// does this frequently. The resolver then finds the author by name. See
/// <c>AuthorResolver.ResolveSourceAuthorsAsync</c>.</summary>
public sealed record SourceAuthorRef(string? Id, string Name);
