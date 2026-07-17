namespace MangaFusion.Sources.Web.Models;

/// <summary>A page of catalogue results — the C# port of Tachiyomi's <c>MangasPage</c>. Web sites
/// paginate by page number + a "has next page" flag rather than by total counts.</summary>
public sealed record MangasPage(IReadOnlyList<WebManga> Mangas, bool HasNextPage);
