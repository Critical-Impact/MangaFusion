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

public class LibraryServiceUpdateChapterTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-libupd-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-libupd-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-libupd-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-libupd-tmp-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private readonly LocalPaths _localPaths;
    private readonly IConfiguration _config;

    public LibraryServiceUpdateChapterTests()
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
        var writers = new ChapterWriterSelector([new CbzChapterWriter(TestPageEncoding.Resolver), new FolderChapterWriter(TestPageEncoding.Resolver)], _config);
        var chapterImporter = new ChapterFileImporter(db, _paths, writers, artifactInspector, pdfExtractor);
        return new(db, _paths, _localPaths, artifactInspector, pdfExtractor, chapterImporter, new AuthorResolver(db), new TagResolver(db));
    }

    private LibraryService NewLibraryService(AppDbContext db)
    {
        var authors = new AuthorResolver(db);
        var tagResolver = new TagResolver(db);
        return new LibraryService(
            db, registry: null!, new ChapterImporter(db),
            new SeriesMetadataApplier(authors, tagResolver),
            new SeriesCoverCache(httpFactory: null!, _paths, NullLogger<SeriesCoverCache>.Instance),
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

    [Fact]
    public async Task UpdateChapter_on_a_manually_imported_chapter_recomputes_sort_key()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var local = NewLocalImport(db);
        var library = NewLibraryService(db);

        var seriesId = await local.CreateSeriesAsync(Meta("Renumbered"));
        var file = await WriteInboxCbzAsync("renumbered", 3);
        await local.ImportAsync(seriesId, new LocalImportRequest(file, "en", [new LocalChapterSpec("1", null, null, 0)]));

        var chapter = await db.Chapters.SingleAsync(c => c.SeriesId == seriesId);

        await library.UpdateChapterAsync(chapter.Id, "2.5", "1", "New Title");

        var updated = await db.Chapters.SingleAsync(c => c.Id == chapter.Id);
        Assert.Equal("2.5", updated.Number);
        Assert.Equal("1", updated.Volume);
        Assert.Equal("New Title", updated.Title);
        Assert.Equal(2.5m, updated.NumberSort);
        Assert.Equal("2.5", updated.NumberKey);
    }

    [Fact]
    public async Task UpdateChapter_rejects_a_chapter_that_is_not_manually_imported()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var library = NewLibraryService(db);

        var series = new Series { Title = "Remote series" };
        var release = new ChapterRelease
        {
            SourceId = "mangadex",
            SourceChapterId = "abc123",
            DiscoveredAt = DateTimeOffset.UtcNow,
        };
        var chapter = new Chapter
        {
            SeriesId = series.Id,
            Language = "en",
            Number = "1",
            NumberSort = 1m,
            NumberKey = "1",
            Releases = [release],
        };
        release.ChapterId = chapter.Id;

        db.Series.Add(series);
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync(); // insert chapter + release first, ActiveReleaseId not yet set

        chapter.ActiveReleaseId = release.Id;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => library.UpdateChapterAsync(chapter.Id, "2", null, null));
        Assert.Contains("manually-imported", ex.Message);
    }

    [Fact]
    public async Task UpdateChapter_rejects_a_number_colliding_with_a_sibling_chapter()
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

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => library.UpdateChapterAsync(chapters[0].Id, "2", null, null));
        Assert.Contains("collides", ex.Message);

        var untouched = await db.Chapters.SingleAsync(c => c.Id == chapters[0].Id);
        Assert.Equal("1", untouched.Number);
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
