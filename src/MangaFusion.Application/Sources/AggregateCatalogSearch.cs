using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Application.Sources;

/// <summary>The virtual "all" source: searches every browsable source at once and interleaves the
/// results. Each site paginates independently with no shared relevance/total, so this fans out for a
/// single page, takes the top <see cref="PerSourceCap"/> hits per source, and round-robin merges them
/// (<see cref="SourceResultMerger"/>). A slow/dead source is skipped, not fatal.</summary>
public sealed class AggregateCatalogSearch(ISourceRegistry registry, ILogger<AggregateCatalogSearch> logger)
{
    /// <summary>Reserved source id the browse UI + endpoints use to request the aggregate search.</summary>
    public const string SourceId = "all";

    /// <summary>How many hits to take from each source per page (interleaved into the merged page).</summary>
    private const int PerSourceCap = 5;
    private const int MaxConcurrency = 8;
    private static readonly TimeSpan PerSourceTimeout = TimeSpan.FromSeconds(8);

    public async Task<PagedResult<SourceSeries>> SearchAsync(
        MediaKind kind, SearchQuery query, CancellationToken ct = default)
    {
        // Only sources that can both describe and download for this library — the browsable catalogue.
        var sources = registry.ForKind(kind)
            .OfType<IMetadataSource>()
            .Where(s => s is IDownloadSource
                        && !string.Equals(s.Id, SourceId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Aggregate page P → each source's page P. Web sources return a full site page regardless of
        // Limit; MangaDex honours it. We take the top PerSourceCap of whatever comes back.
        var page = query.Limit > 0 ? query.Offset / query.Limit : 0;
        var perSourceQuery = query with { Offset = page * PerSourceCap, Limit = PerSourceCap };

        using var gate = new SemaphoreSlim(MaxConcurrency);
        var fetches = sources.Select(s => FetchAsync(s, perSourceQuery, gate, ct));
        var results = await Task.WhenAll(fetches);

        var perSource = new List<IReadOnlyList<SourceSeries>>(results.Length);
        var hasNext = false;
        foreach (var result in results)
        {
            if (result is null || result.Items.Count == 0) continue;
            perSource.Add(result.Items.Take(PerSourceCap).ToList());
            // The source has further pages to dig into.
            if (result.Total > result.Offset + result.Items.Count) hasNext = true;
        }

        var merged = SourceResultMerger.Interleave(perSource);
        // There's no true global total, and the merged page size varies. Report a *page-aligned* total so
        // the frontend's ceil(total / pageSize) math yields a working next button: exactly "one page" per
        // browse page, plus one extra item when another page exists. This drives a prev/next pager
        // ("Page N of N+1") without a real count — the same effect the single web sources already produce.
        var total = (page + 1) * Math.Max(query.Limit, 1) + (hasNext ? 1 : 0);
        return new PagedResult<SourceSeries>(merged, total, query.Limit, query.Offset);
    }

    private async Task<PagedResult<SourceSeries>?> FetchAsync(
        IMetadataSource source, SearchQuery query, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PerSourceTimeout);
            return await source.SearchAsync(query, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // A single misbehaving source (down, 403, timeout, parse error) must not sink the whole
            // aggregate search — drop it and keep the healthy results.
            logger.LogDebug(ex, "Aggregate search: source '{SourceId}' failed or timed out; skipping.", source.Id);
            return null;
        }
        finally
        {
            gate.Release();
        }
    }
}
