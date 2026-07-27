using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ContractKind = MangaFusion.Contracts.Models.MediaKind;
using DomainKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.IntegrationTests;

/// <summary>The library a matched import lands in must come from the batch (the user's mode + per-kind
/// inbox), not from the metadata source's per-series kind guess — otherwise a light-novel import whose
/// MangaUpdates match isn't typed exactly "Novel" silently lands in the manga library (the bug that put
/// 7th Time Loop in Manga mode).</summary>
public class ImportKindTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-importkind-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-importkind-lib-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;

    public ImportKindTests()
    {
        Directory.CreateDirectory(_root);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Library:RootPath"] = _root,
                ["Library:TempPath"] = Path.Combine(_root, "tmp"),
            })
            .Build();
        _paths = new LibraryPaths(config);
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private LibraryService NewLibrary(AppDbContext db)
    {
        var authors = new AuthorResolver(db);
        var tagResolver = new TagResolver(db);
        return new LibraryService(
            db, new SourceRegistry([new FakeMangaUpdates()]), new ChapterImporter(db),
            new SeriesMetadataApplier(authors, tagResolver),
            new SeriesCoverCache(
                httpFactory: null!, _paths,
                new CollectionCoverComposer(_paths, NullLogger<CollectionCoverComposer>.Instance),
                NullLogger<SeriesCoverCache>.Instance),
            tagResolver, _paths);
    }

    [Fact]
    public async Task Forced_kind_wins_over_the_sources_primary_kind()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // The match reports no per-series kind (type isn't "Novel"), so KindOf would fall back to the
        // source's primary kind (Manga) — but the light-novel batch's kind is forced through.
        var id = await NewLibrary(db).AddOrUpdateMetadataOnlyAsync("mangaupdates", "123", DomainKind.LightNovel);

        var series = await db.Series.SingleAsync(s => s.Id == id);
        Assert.Equal(DomainKind.LightNovel, series.Kind);
    }

    [Fact]
    public async Task Without_a_forced_kind_it_falls_back_to_the_source_primary()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // No forced kind (the browse "add to library" path) — the unmatched hint is null, so it lands in
        // the source's primary library. This is the behaviour the import path deliberately overrides.
        var id = await NewLibrary(db).AddOrUpdateMetadataOnlyAsync("mangaupdates", "123");

        var series = await db.Series.SingleAsync(s => s.Id == id);
        Assert.Equal(DomainKind.Manga, series.Kind);
    }

    [Fact]
    public async Task Same_source_entry_resolves_to_a_distinct_series_per_kind()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var library = NewLibrary(db);

        // One MangaUpdates entry, imported once as manga and once as a light novel — the real case where a
        // series has no separate LN listing, so both matches point at the same id. Each kind must get its
        // own library series (this is what stops the LN import's chapters landing in the manga series and
        // colliding), and the per-kind unique index must permit both links to the same source entry.
        var mangaId = await library.AddOrUpdateMetadataOnlyAsync("mangaupdates", "123", DomainKind.Manga);
        var novelId = await library.AddOrUpdateMetadataOnlyAsync("mangaupdates", "123", DomainKind.LightNovel);

        Assert.NotEqual(mangaId, novelId);

        // Re-resolving an already-linked kind returns the same series, not yet a third one.
        var mangaAgain = await library.AddOrUpdateMetadataOnlyAsync("mangaupdates", "123", DomainKind.Manga);
        Assert.Equal(mangaId, mangaAgain);

        var series = await db.Series.Include(s => s.SourceLinks)
            .Where(s => s.Id == mangaId || s.Id == novelId)
            .ToListAsync();
        Assert.Equal(DomainKind.Manga, series.Single(s => s.Id == mangaId).Kind);
        Assert.Equal(DomainKind.LightNovel, series.Single(s => s.Id == novelId).Kind);
        // Both link to the same source entry, each stamped with its owning series' kind.
        Assert.All(series, s => Assert.Equal("123", s.SourceLinks.Single().SourceSeriesId));
        Assert.All(series, s => Assert.Equal(s.Kind, s.SourceLinks.Single().Kind));
    }

    [Fact]
    public async Task Batch_list_is_scoped_to_the_library()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.ImportBatches.Add(new ImportBatch { Kind = DomainKind.Manga });
        db.ImportBatches.Add(new ImportBatch { Kind = DomainKind.LightNovel });
        await db.SaveChangesAsync();

        // Only `db` is exercised by ListBatchesAsync, so the other dependencies can be null here.
        var service = new ImportService(db, null!, null!, null!, null!, null!, null!, null!, NullLogger<ImportService>.Instance);

        var novels = await service.ListBatchesAsync(DomainKind.LightNovel);
        var manga = await service.ListBatchesAsync(DomainKind.Manga);

        Assert.All(novels, b => Assert.Equal("LightNovel", b.Kind));
        Assert.Single(novels);
        Assert.All(manga, b => Assert.Equal("Manga", b.Kind));
        Assert.Single(manga);
    }

    /// <summary>MangaUpdates-shaped: serves both manga and light novels, but this particular series reports
    /// no per-series kind hint (as if its type string weren't recognised as a novel).</summary>
    private sealed class FakeMangaUpdates : IMetadataSource
    {
        public string Id => "mangaupdates";
        public string DisplayName => "MangaUpdates";
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;
        public IReadOnlyList<ContractKind> SupportedKinds => [ContractKind.Manga, ContractKind.LightNovel];

        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            Task.FromResult(new PagedResult<SourceSeries>([], 0, query.Limit, query.Offset));

        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            Task.FromResult<SourceSeries?>(new SourceSeries
            {
                SourceId = "mangaupdates", SourceSeriesId = sourceSeriesId, Title = "7th Time Loop", Kind = null,
            });

        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SourceTag>>([]);
    }
}
