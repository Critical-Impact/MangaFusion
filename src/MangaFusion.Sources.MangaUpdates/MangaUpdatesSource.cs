using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.MangaUpdates.Http;
using MangaFusion.Sources.MangaUpdates.Mapping;

namespace MangaFusion.Sources.MangaUpdates;

/// <summary>The MangaUpdates source: metadata only (no chapter/download capability — MangaUpdates has
/// no chapter API). Backed by the public, unauthenticated MangaUpdates API.</summary>
public sealed class MangaUpdatesSource(MangaUpdatesApiClient api) : IMetadataSource
{
    public string Id => MangaUpdatesConstants.SourceId;

    public string DisplayName => MangaUpdatesConstants.DisplayName;

    public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

    // MangaUpdates lists both manga and light novels (its "Novel" type). Manga stays first so it remains
    // the primary/fallback kind; per-series routing to the light-novel library is by SourceSeries.Kind.
    public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga, MediaKind.LightNovel];

    public async Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var dto = await api.SearchSeriesAsync(query, ct);
        var records = dto?.Results.Select(r => r.Record).OfType<Dtos.SeriesModelDto>().ToList() ?? [];

        // MangaUpdates' search response is a lean summary (its own OpenAPI spec names it
        // SeriesModelSearchV1) that omits associated/alt titles and authors entirely — only the full
        // GET /series/{id} response (SeriesModelV1) has them. Alt-titles are the entire reason this
        // source was chosen (they're what let a release's English title match MangaUpdates' often
        // romanized-Japanese primary title — see the ImportMatcher notes), so enrich every search
        // result with a detail fetch rather than silently degrading to primary-title-only matching.
        // Best-effort per result: a failed detail fetch falls back to the lean record instead of
        // failing the whole search.
        var enriched = await Task.WhenAll(records.Select(async record =>
        {
            try
            {
                return await api.GetSeriesAsync(record.SeriesId.ToString(), ct) ?? record;
            }
            catch
            {
                return record;
            }
        }));

        var items = enriched.Select(MangaUpdatesMapper.ToSeries).ToList();
        return new PagedResult<SourceSeries>(items, dto?.TotalHits ?? 0, dto?.PerPage ?? query.Limit, query.Offset);
    }

    public async Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default)
    {
        var dto = await api.GetSeriesAsync(sourceSeriesId, ct);
        return dto is null ? null : MangaUpdatesMapper.ToSeries(dto);
    }

    public async Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default)
    {
        var dto = await api.GetGenresAsync(ct);
        return dto.Select(MangaUpdatesMapper.ToTag).ToList();
    }
}
