using MangaFusion.Contracts.Models;

namespace MangaFusion.Contracts.Sources;

/// <summary>A source that can search for and describe series.</summary>
public interface IMetadataSource : ISource
{
    Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default);

    Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default);

    /// <summary>Lists the source's filterable tags (genres, themes, …) for browsing.</summary>
    Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default);
}
