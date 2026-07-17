using MangaFusion.Application.Library;
using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Sources;

/// <summary>Thin orchestration over the source registry for browsing (search / series / chapters).
/// Keeps endpoints from touching the registry and source contracts directly.</summary>
public sealed class CatalogService(ISourceRegistry registry, ILibraryService library, AggregateCatalogSearch aggregate)
{
    /// <summary>Searches a single source, or — for the reserved <see cref="AggregateCatalogSearch.SourceId"/>
    /// ("all") — fans out across every browsable source for <paramref name="kind"/> and interleaves.</summary>
    public Task<PagedResult<SourceSeries>> SearchAsync(
        string sourceId, SearchQuery query, MediaKind kind = MediaKind.Manga, CancellationToken ct = default) =>
        string.Equals(sourceId, AggregateCatalogSearch.SourceId, StringComparison.OrdinalIgnoreCase)
            ? aggregate.SearchAsync(kind, query, ct)
            : registry.GetMetadataSource(sourceId).SearchAsync(query, ct);

    public Task<SourceSeries?> GetSeriesAsync(
        string sourceId, string seriesId, CancellationToken ct = default) =>
        registry.GetMetadataSource(sourceId).GetSeriesAsync(seriesId, ct);

    /// <summary>Reads the source's tag registry from our local cache (kept in sync by a background
    /// job) rather than the source's live API — falls back to a live call only if nothing's cached
    /// yet for this source (e.g. before the first sync has run).</summary>
    public async Task<IReadOnlyList<SourceTag>> GetTagsAsync(string sourceId, CancellationToken ct = default)
    {
        // The aggregate "all" source has no shared tag vocabulary — no facets in the UI.
        if (string.Equals(sourceId, AggregateCatalogSearch.SourceId, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var cached = await library.GetCachedSourceTagsAsync(sourceId, ct);
        return cached.Count > 0 ? cached : await registry.GetMetadataSource(sourceId).GetTagsAsync(ct);
    }

    public Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceId, string seriesId, ChapterQuery query, CancellationToken ct = default) =>
        registry.GetChapterSource(sourceId).GetChaptersAsync(seriesId, query, ct);

    /// <summary>Resolves a chapter's page images live from the source (no download) — backs the
    /// preview reader. Throws <see cref="SourceCapabilityException"/> if the source can't download.</summary>
    public Task<SourcePageSet> GetPagesAsync(
        string sourceId, string sourceChapterId, PageQuality quality = PageQuality.Original,
        CancellationToken ct = default) =>
        registry.GetDownloadSource(sourceId).GetPagesAsync(sourceChapterId, quality, ct);
}
