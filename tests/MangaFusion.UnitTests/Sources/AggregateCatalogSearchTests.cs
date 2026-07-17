using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using DomainMediaKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.UnitTests.Sources;

public class SourceResultMergerTests
{
    [Fact]
    public void Interleave_is_round_robin_across_uneven_lists()
    {
        IReadOnlyList<string>[] lists = [["a1", "a2", "a3"], ["b1"], ["c1", "c2"]];

        var merged = SourceResultMerger.Interleave(lists);

        Assert.Equal(["a1", "b1", "c1", "a2", "c2", "a3"], merged);
    }

    [Fact]
    public void Interleave_of_nothing_is_empty()
    {
        Assert.Empty(SourceResultMerger.Interleave(Array.Empty<IReadOnlyList<string>>()));
    }
}

public class AggregateCatalogSearchTests
{
    [Fact]
    public async Task Interleaves_top_k_per_source_and_skips_failures()
    {
        // 'a' returns a full site page (20); 'b' returns 3; 'bad' throws; 'slow' simulates a timeout.
        var registry = new FakeRegistry(
            Source("a", (q, _) => Result("a", q.Offset, count: 20, hasMore: true)),
            Source("b", (q, _) => Result("b", q.Offset, count: 3, hasMore: false)),
            Source("bad", (_, _) => throw new InvalidOperationException("boom")),
            Source("slow", (_, _) => throw new OperationCanceledException()));
        var aggregate = new AggregateCatalogSearch(registry, NullLogger<AggregateCatalogSearch>.Instance);

        var result = await aggregate.SearchAsync(DomainMediaKind.Manga, new SearchQuery { Limit = 24, Offset = 0 });

        // top-5 of 'a' + top-3 of 'b', round-robin; the two broken sources contribute nothing.
        Assert.Equal(
            ["a-0", "b-0", "a-1", "b-1", "a-2", "b-2", "a-3", "a-4"],
            result.Items.Select(i => i.SourceSeriesId));
        Assert.True(result.Total > result.Limit); // page-aligned total > one page → next page available
    }

    [Fact]
    public async Task No_next_page_when_no_source_has_more()
    {
        var registry = new FakeRegistry(
            Source("a", (q, _) => Result("a", q.Offset, count: 2, hasMore: false)),
            Source("b", (q, _) => Result("b", q.Offset, count: 1, hasMore: false)));
        var aggregate = new AggregateCatalogSearch(registry, NullLogger<AggregateCatalogSearch>.Instance);

        var result = await aggregate.SearchAsync(DomainMediaKind.Manga, new SearchQuery { Limit = 24, Offset = 0 });

        Assert.Equal(3, result.Items.Count);
        Assert.Equal(result.Limit, result.Total); // exactly one page → no next page (page 0, limit 24)
    }

    [Fact]
    public async Task Page_two_advances_each_source_by_the_per_source_cap()
    {
        SearchQuery? captured = null;
        var registry = new FakeRegistry(
            Source("a", (q, _) => { captured = q; return Result("a", q.Offset, count: 5, hasMore: true); }));
        var aggregate = new AggregateCatalogSearch(registry, NullLogger<AggregateCatalogSearch>.Instance);

        // Browse "page 2" sends offset = pageSize (24). Aggregate page P=1 → per-source offset = 1 * capK.
        await aggregate.SearchAsync(DomainMediaKind.Manga, new SearchQuery { Limit = 24, Offset = 24 });

        Assert.NotNull(captured);
        Assert.Equal(5, captured!.Offset); // per-source cap K = 5
        Assert.Equal(5, captured.Limit);
    }

    private static PagedResult<SourceSeries> Result(string sourceId, int offset, int count, bool hasMore)
    {
        var items = Enumerable.Range(0, count)
            .Select(i => new SourceSeries
            {
                SourceId = sourceId,
                SourceSeriesId = $"{sourceId}-{offset + i}",
                Title = $"{sourceId} {offset + i}",
            })
            .ToList();
        return new PagedResult<SourceSeries>(items, offset + count + (hasMore ? 1 : 0), count, offset);
    }

    private static FakeSource Source(
        string id, Func<SearchQuery, CancellationToken, PagedResult<SourceSeries>> search) => new(id, search);

    private sealed class FakeSource(
        string id, Func<SearchQuery, CancellationToken, PagedResult<SourceSeries>> search)
        : IMetadataSource, IDownloadSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata | SourceCapabilities.Download;
        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(search(query, ct));

        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourceTag>>([]);

        public Task<SourcePageSet> GetPagesAsync(
            string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRegistry(params ISource[] sources) : ISourceRegistry
    {
        public IReadOnlyList<ISource> All => sources;
        public IReadOnlyList<ISource> ForKind(DomainMediaKind kind) => sources;
        public bool Contains(string id) => sources.Any(s => s.Id == id);
        public ISource Get(string id) => sources.First(s => s.Id == id);
        public IMetadataSource GetMetadataSource(string id) => (IMetadataSource)Get(id);
        public IChapterSource GetChapterSource(string id) => (IChapterSource)Get(id);
        public IDownloadSource GetDownloadSource(string id) => (IDownloadSource)Get(id);
    }
}
