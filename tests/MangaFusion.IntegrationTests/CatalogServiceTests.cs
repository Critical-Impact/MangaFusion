using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MediaKind = MangaFusion.Contracts.Models.MediaKind;

namespace MangaFusion.IntegrationTests;

public class CatalogServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-catalog-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-catalog-lib-{Guid.NewGuid():N}");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private LibraryService NewLibraryService(AppDbContext db, ISourceRegistry registry)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _root })
            .Build();
        var paths = new LibraryPaths(config);
        var authors = new AuthorResolver(db);
        var tagResolver = new TagResolver(db);
        return new LibraryService(
            db, registry, new ChapterImporter(db),
            new SeriesMetadataApplier(authors, tagResolver),
            new SeriesCoverCache(
                httpFactory: null!, paths,
                new CollectionCoverComposer(paths, NullLogger<CollectionCoverComposer>.Instance),
                NullLogger<SeriesCoverCache>.Instance),
            tagResolver, paths);
    }

    private sealed class CountingMetadataSource(string id, IReadOnlyList<SourceTag> tags) : IMetadataSource
    {
        public int GetTagsAsyncCalls { get; private set; }
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];
        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default)
        {
            GetTagsAsyncCalls++;
            return Task.FromResult(tags);
        }
    }

    private sealed class FakeRegistry(IMetadataSource source) : ISourceRegistry
    {
        public IReadOnlyList<ISource> All => [source];

        public IReadOnlyList<ISource> ForKind(MangaFusion.Domain.Library.MediaKind kind) =>
            source.SupportedKinds.Contains(MediaKinds.ToContract(kind)) ? [source] : [];

        public bool Contains(string id) => source.Id == id;
        public ISource Get(string id) => source.Id == id ? source : throw new InvalidOperationException();
        public IMetadataSource GetMetadataSource(string id) =>
            source.Id == id ? source : throw new InvalidOperationException();
        public IChapterSource GetChapterSource(string id) => throw new NotSupportedException();
        public IDownloadSource GetDownloadSource(string id) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Returns_cached_tags_without_calling_the_live_source()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Tags.Add(new Tag { Name = "Action", Group = "genre", SourceId = "fake", SourceTagId = "t1" });
        await db.SaveChangesAsync();

        var source = new CountingMetadataSource("fake", [new SourceTag("t-live", "Should not appear", "genre")]);
        var registry = new FakeRegistry(source);
        var catalog = new CatalogService(registry, NewLibraryService(db, registry),
            new AggregateCatalogSearch(registry, NullLogger<AggregateCatalogSearch>.Instance));

        var tags = await catalog.GetTagsAsync("fake");

        Assert.Equal(["t1"], tags.Select(t => t.Id));
        Assert.Equal(0, source.GetTagsAsyncCalls);
    }

    [Fact]
    public async Task Falls_back_to_the_live_source_when_nothing_is_cached_yet()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var source = new CountingMetadataSource("fake", [new SourceTag("t-live", "Action", "genre")]);
        var registry = new FakeRegistry(source);
        var catalog = new CatalogService(registry, NewLibraryService(db, registry),
            new AggregateCatalogSearch(registry, NullLogger<AggregateCatalogSearch>.Instance));

        var tags = await catalog.GetTagsAsync("fake");

        Assert.Equal(["t-live"], tags.Select(t => t.Id));
        Assert.Equal(1, source.GetTagsAsyncCalls);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
