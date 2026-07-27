using MangaFusion.Application.Library;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

public class LibraryServiceDeleteTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-libdel-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-libdel-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-libdel-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-libdel-tmp-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private LocalPaths _localPaths = null!;
    private readonly IConfiguration _config;

    public LibraryServiceDeleteTests()
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
        var proseImporter = new ProseChapterImporter(db, _paths, new EpubChapterWriter());
        return new(db, _paths, _localPaths, artifactInspector, pdfExtractor, cbrExtractor, epubExtractor, chapterImporter, proseImporter, new AuthorResolver(db), new TagResolver(db));
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
                // The local inbox is split per library — `_inbox` is now the parent of manga/ and comics/,
                // not a directory the importer reads from directly.
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

    [Fact]
    public async Task DeleteSeries_removes_chapters_artifacts_and_files_on_disk()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Doomed"));
        var file = await WriteInboxCbzAsync("doomed", 3);
        await local.ImportAsync(seriesId, new LocalImportRequest(file, "en", [new LocalChapterSpec("1", null, null, 0)]));

        var artifact = await db.Artifacts.SingleAsync(a => a.SeriesId == seriesId);
        var artifactPath = _paths.Absolute(MediaKind.Manga, artifact.Path);
        Assert.True(File.Exists(artifactPath));

        await library.DeleteSeriesAsync(seriesId);

        Assert.False(await db.Series.AnyAsync(s => s.Id == seriesId));
        Assert.False(await db.Chapters.AnyAsync(c => c.SeriesId == seriesId));
        Assert.False(await db.Artifacts.AnyAsync(a => a.SeriesId == seriesId));
        Assert.False(File.Exists(artifactPath));
    }

    [Fact]
    public async Task DeleteChapter_that_solely_owns_its_artifact_deletes_the_file_too()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Solo"));
        var file = await WriteInboxCbzAsync("solo", 3);
        await local.ImportAsync(seriesId, new LocalImportRequest(file, "en", [new LocalChapterSpec("1", null, null, 0)]));

        var chapter = await db.Chapters.SingleAsync(c => c.SeriesId == seriesId);
        var artifact = await db.Artifacts.SingleAsync(a => a.SeriesId == seriesId);
        var artifactPath = _paths.Absolute(MediaKind.Manga, artifact.Path);

        await library.DeleteChapterAsync(chapter.Id);

        Assert.False(await db.Chapters.AnyAsync(c => c.Id == chapter.Id));
        Assert.False(await db.Artifacts.AnyAsync(a => a.Id == artifact.Id));
        Assert.False(File.Exists(artifactPath));
        Assert.True(await db.Series.AnyAsync(s => s.Id == seriesId)); // the series itself survives
    }

    [Fact]
    public async Task DeleteChapter_sharing_an_artifact_with_a_surviving_chapter_keeps_the_file()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Volume"));
        var file = await WriteInboxCbzAsync("vol", 5); // one 5-page file, carved into two chapters
        await local.ImportAsync(seriesId, new LocalImportRequest(file, "en",
            [new LocalChapterSpec("1", null, null, 2), new LocalChapterSpec("2", null, null, 3)]));

        var chapters = await db.Chapters.Where(c => c.SeriesId == seriesId).OrderBy(c => c.NumberSort).ToListAsync();
        Assert.Equal(2, chapters.Count);
        var artifact = await db.Artifacts.SingleAsync(a => a.SeriesId == seriesId);
        var artifactPath = _paths.Absolute(MediaKind.Manga, artifact.Path);

        await library.DeleteChapterAsync(chapters[0].Id);

        Assert.False(await db.Chapters.AnyAsync(c => c.Id == chapters[0].Id));
        Assert.True(await db.Chapters.AnyAsync(c => c.Id == chapters[1].Id)); // survives
        Assert.True(await db.Artifacts.AnyAsync(a => a.Id == artifact.Id)); // still needed by chapter 2
        Assert.True(File.Exists(artifactPath)); // file itself untouched — chapter 2 still reads from it

        var remainingLink = await db.ArtifactChapters.SingleAsync(l => l.ArtifactId == artifact.Id);
        Assert.Equal(chapters[1].Id, remainingLink.ChapterId);
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
