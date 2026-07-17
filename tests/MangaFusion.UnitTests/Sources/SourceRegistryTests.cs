using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using DomainKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.UnitTests.Sources;

public class SourceRegistryTests
{
    private sealed class FakeMetaSource : IMetadataSource
    {
        public string Id => "meta";
        public string DisplayName => "Meta";
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SourceSeries>([], 0, query.Limit, query.Offset));

        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            Task.FromResult<SourceSeries?>(null);

        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourceTag>>([]);
    }

    private sealed class FakePlainSource : ISource
    {
        public string Id => "plain";
        public string DisplayName => "Plain";
        public SourceCapabilities Capabilities => SourceCapabilities.None;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];
    }

    /// <summary>ComicVine-shaped: serves the comic library only.</summary>
    private sealed class FakeComicSource : ISource
    {
        public string Id => "comicvine";
        public string DisplayName => "ComicVine";
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Comic];
    }

    [Fact]
    public void ForKind_offers_only_the_sources_that_serve_that_library()
    {
        var registry = new SourceRegistry([new FakeMetaSource(), new FakeComicSource()]);

        Assert.Equal(["meta"], registry.ForKind(DomainKind.Manga).Select(s => s.Id));
        Assert.Equal(["comicvine"], registry.ForKind(DomainKind.Comic).Select(s => s.Id));
    }

    [Fact]
    public void Get_returns_registered_source()
    {
        var registry = new SourceRegistry([new FakeMetaSource(), new FakePlainSource()]);

        Assert.Equal("meta", registry.Get("meta").Id);
        Assert.True(registry.Contains("plain"));
        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void Get_unknown_id_throws() =>
        Assert.Throws<SourceNotFoundException>(() => new SourceRegistry([]).Get("nope"));

    [Fact]
    public void GetMetadataSource_throws_when_capability_missing()
    {
        var registry = new SourceRegistry([new FakePlainSource()]);
        Assert.Throws<SourceCapabilityException>(() => registry.GetMetadataSource("plain"));
    }

    [Fact]
    public void GetMetadataSource_returns_capable_source()
    {
        var registry = new SourceRegistry([new FakeMetaSource()]);
        Assert.Equal("meta", registry.GetMetadataSource("meta").Id);
    }

    [Fact]
    public void Lookup_is_case_insensitive()
    {
        var registry = new SourceRegistry([new FakeMetaSource()]);
        Assert.Equal("meta", registry.Get("META").Id);
    }
}
