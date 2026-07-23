using MangaFusion.Application.Library;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

public class LibraryServiceSortModeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-sortmode-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-sortmode-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-sortmode-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-sortmode-tmp-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private readonly LocalPaths _localPaths;
    private readonly IConfiguration _config;

    public LibraryServiceSortModeTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_inbox);
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Library:RootPath"] = _root,
                ["Library:TempPath"] = _tempRoot,
                ["LocalImport:InboxPath"] = _inbox,
            })
            .Build();
        _paths = new LibraryPaths(_config);
        _localPaths = new LocalPaths(_config);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private LocalImportService NewLocalImport(AppDbContext db)
    {
        var artifactInspector = new ArtifactFileInspector();
        var pdfExtractor = new PdfPageExtractor(_config);
        var cbrExtractor = new CbrPageExtractor();
        var epubExtractor = new EpubPageExtractor();
        var writers = new ChapterWriterSelector([new CbzChapterWriter(TestPageEncoding.Resolver), new FolderChapterWriter(TestPageEncoding.Resolver)], _config);
        var chapterImporter = new ChapterFileImporter(db, _paths, writers, artifactInspector, pdfExtractor, cbrExtractor, epubExtractor);
        return new(db, _paths, _localPaths, artifactInspector, pdfExtractor, cbrExtractor, epubExtractor, chapterImporter, new AuthorResolver(db), new TagResolver(db));
    }

    private LibraryService NewLibraryService(AppDbContext db)
    {
        var authors = new AuthorResolver(db);
        var tagResolver = new TagResolver(db);
        return new LibraryService(
            db, registry: null!, new ChapterImporter(db),
            new SeriesMetadataApplier(authors, tagResolver),
            new SeriesCoverCache(
                httpFactory: null!, _paths,
                new CollectionCoverComposer(_paths, NullLogger<CollectionCoverComposer>.Instance),
                NullLogger<SeriesCoverCache>.Instance),
            tagResolver, _paths);
    }

    private async Task<string> WriteInboxCbzAsync(string baseName, params int[] segmentPageCounts)
    {
        var segments = new List<ChapterSegment>();
        var marker = 0;
        foreach (var count in segmentPageCounts)
        {
            var files = new List<PageFile>();
            for (var i = 0; i < count; i++)
            {
                var src = Path.Combine(_localPaths.InboxRoot(MediaKind.Manga), $"src-{baseName}-{marker}.jpg");
                await File.WriteAllBytesAsync(src, [0xFF, 0xD8, (byte)marker]);
                files.Add(new PageFile(i, $"{i}.jpg", src));
                marker++;
            }

            segments.Add(new ChapterSegment("1", null, null, "en", null, files));
        }

        var inboxRoot = _localPaths.InboxRoot(MediaKind.Manga);
        await new CbzChapterWriter(TestPageEncoding.Resolver).WriteAsync(
            new WriteRequest("x", [], [], StorageFormat.Cbz, inboxRoot, baseName, segments));

        foreach (var tmp in Directory.EnumerateFiles(inboxRoot, $"src-{baseName}-*.jpg"))
        {
            File.Delete(tmp);
        }

        return baseName + ".cbz";
    }

    private static LocalSeriesMetadata Meta(string title) =>
        new(title, null, ["Some Author"], ["Action"], "A manual series", "Safe", "Completed", 2021, "ja", null);

    /// <summary>Imports one whole-volume CBZ (blank number) or one numbered extra, matching the real
    /// production scenario (a series with several volume-compilation files plus an individually-numbered
    /// extra tagged to a specific volume).</summary>
    private async Task ImportOneAsync(
        LocalImportService local, Guid seriesId, string baseName, string? number, string? volume)
    {
        var file = await WriteInboxCbzAsync(baseName, 3);
        await local.ImportAsync(seriesId, new LocalImportRequest(file, "en", [new LocalChapterSpec(number, volume, null, 0)]));
    }

    [Fact]
    public async Task SetChapterSortMode_recomputes_keys_and_volume_sort_for_every_chapter()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Recompute"));
        await ImportOneAsync(local, seriesId, "v1", null, "1");
        await ImportOneAsync(local, seriesId, "v2", null, "2");
        await ImportOneAsync(local, seriesId, "extra", "1", "2"); // an extra tagged to volume 2

        await library.SetChapterSortModeAsync(seriesId, ChapterSortMode.VolumeThenChapter);

        var chapters = await db.Chapters.Where(c => c.SeriesId == seriesId).ToListAsync();
        var vol1 = chapters.Single(c => c.Volume == "1" && c.Number == null);
        var vol2 = chapters.Single(c => c.Volume == "2" && c.Number == null);
        var extra = chapters.Single(c => c.Number == "1");

        Assert.Equal("1:vol-1", vol1.NumberKey);
        Assert.Equal(1m, vol1.VolumeSort);
        Assert.Equal("2:vol-2", vol2.NumberKey);
        Assert.Equal(2m, vol2.VolumeSort);
        Assert.Equal("2:1", extra.NumberKey);
        Assert.Equal(2m, extra.VolumeSort);

        var series = await db.Series.SingleAsync(s => s.Id == seriesId);
        Assert.Equal(ChapterSortMode.VolumeThenChapter, series.SortMode);

        // Switching back to Absolute restores the original (unqualified) keys.
        await library.SetChapterSortModeAsync(seriesId, ChapterSortMode.Absolute);
        var reverted = await db.Chapters.SingleAsync(c => c.Id == extra.Id);
        Assert.Equal("1", reverted.NumberKey);
    }

    [Fact]
    public async Task SetChapterSortMode_is_a_noop_when_already_in_that_mode()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Noop"));
        await ImportOneAsync(local, seriesId, "v1", null, "1");

        var before = await db.Chapters.Select(c => c.NumberKey).ToListAsync();
        await library.SetChapterSortModeAsync(seriesId, ChapterSortMode.Absolute); // already Absolute
        var after = await db.Chapters.Select(c => c.NumberKey).ToListAsync();

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task SetChapterSortMode_rejects_a_switch_that_would_merge_two_chapters()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Collision"));

        // A collision can only be *introduced* by a mode switch in the Volume→Absolute direction:
        // Absolute's own unique index already guarantees no two chapters share a raw key while in
        // Absolute mode, and qualifying with a volume prefix can only ever preserve that uniqueness,
        // never break it. So to get two chapters that legitimately coexist (different volumes, same
        // number) we must switch to VolumeThenChapter *first*, then import them, then try reverting.
        await library.SetChapterSortModeAsync(seriesId, ChapterSortMode.VolumeThenChapter);
        await ImportOneAsync(local, seriesId, "a", "1", "5");
        await ImportOneAsync(local, seriesId, "b", "1", "8");

        var beforeKeys = await db.Chapters.Select(c => c.NumberKey).OrderBy(k => k).ToListAsync();
        Assert.Equal(["5:1", "8:1"], beforeKeys);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => library.SetChapterSortModeAsync(seriesId, ChapterSortMode.Absolute));
        Assert.Contains("merge", ex.Message);

        var series = await db.Series.SingleAsync(s => s.Id == seriesId);
        Assert.Equal(ChapterSortMode.VolumeThenChapter, series.SortMode);
        var afterKeys = await db.Chapters.Select(c => c.NumberKey).OrderBy(k => k).ToListAsync();
        Assert.Equal(beforeKeys, afterKeys); // untouched — no partial mutation on failure
    }

    [Fact]
    public async Task VolumeThenChapter_mode_sorts_an_extra_right_after_its_tagged_volume()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);
        var reader = new ReaderService(db, readers: null!, _paths);

        var seriesId = await local.CreateSeriesAsync(Meta("Hokkaido-like"));
        await ImportOneAsync(local, seriesId, "v1", null, "1");
        await ImportOneAsync(local, seriesId, "v2", null, "2");
        await ImportOneAsync(local, seriesId, "extra", "1", "2"); // belongs right after volume 2
        await ImportOneAsync(local, seriesId, "v3", null, "3");

        // Absolute mode (today's unchanged behavior) reproduces the real bug: the extra's own Number=1
        // sorts it in with/before volume 1, nowhere near volume 2.
        var chapters = await db.Chapters.Where(c => c.SeriesId == seriesId).ToListAsync();
        var vol1 = chapters.Single(c => c.Volume == "1" && c.Number == null);
        var vol2 = chapters.Single(c => c.Volume == "2" && c.Number == null);
        var vol3 = chapters.Single(c => c.Volume == "3" && c.Number == null);
        var extraChapter = chapters.Single(c => c.Number == "1");

        var absoluteOrder = await reader.GetNeighborsAsync(vol1.Id);
        // vol1 (Sort=1, Key="vol-1") ties on Sort with extra (Sort=1, Key="1"); "1" sorts alphabetically
        // before "vol-1", so the extra actually lands *before* volume 1 — the real bug this feature fixes.
        Assert.Equal(extraChapter.Id, absoluteOrder.PrevChapterId);

        await library.SetChapterSortModeAsync(seriesId, ChapterSortMode.VolumeThenChapter);

        var atVol2 = await reader.GetNeighborsAsync(vol2.Id);
        Assert.Equal(vol1.Id, atVol2.PrevChapterId);
        Assert.Equal(extraChapter.Id, atVol2.NextChapterId);

        var atExtra = await reader.GetNeighborsAsync(extraChapter.Id);
        Assert.Equal(vol2.Id, atExtra.PrevChapterId);
        Assert.Equal(vol3.Id, atExtra.NextChapterId);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }

        foreach (var dir in new[] { _root, _inbox, _tempRoot })
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }
}
