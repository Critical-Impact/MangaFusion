using MangaFusion.Application.Library;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.IntegrationTests;

public class LocalImportServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-local-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-local-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-local-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-local-tmp-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private readonly LocalPaths _localPaths;
    private readonly IConfiguration _config;

    public LocalImportServiceTests()
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

    /// <summary>The inbox is split per library, so files have to be staged in the right half of it —
    /// `_inbox` itself is now just the parent of `manga/` and `comics/`.</summary>
    private string InboxRoot(MediaKind kind = MediaKind.Manga) => _localPaths.InboxRoot(kind);

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private LocalImportService NewService(AppDbContext db)
    {
        var artifactInspector = new ArtifactFileInspector();
        var pdfExtractor = new PdfPageExtractor(_config);
        var cbrExtractor = new CbrPageExtractor();
        var epubExtractor = new EpubPageExtractor();
        var writers = new ChapterWriterSelector([new CbzChapterWriter(TestPageEncoding.Resolver), new FolderChapterWriter(TestPageEncoding.Resolver)], _config);
        var chapterImporter = new ChapterFileImporter(db, _paths, writers, artifactInspector, pdfExtractor, cbrExtractor, epubExtractor);
        return new(db, _paths, _localPaths, artifactInspector, pdfExtractor, cbrExtractor, epubExtractor, chapterImporter, new AuthorResolver(db), new TagResolver(db));
    }

    private ReaderService NewReader(AppDbContext db) =>
        new(db, new ArtifactReaderRegistry([new CbzArtifactReader(), new FolderArtifactReader(_paths)]), _paths);

    private Task<string> WriteInboxCbzAsync(string baseName, params int[] segmentPageCounts) =>
        WriteInboxCbzAsync(baseName, MediaKind.Manga, segmentPageCounts);

    /// <summary>Writes a CBZ into that library's half of the inbox, with one segment per page-count; page
    /// byte markers equal the global page index. Returns the inbox-relative file name.</summary>
    private async Task<string> WriteInboxCbzAsync(
        string baseName, MediaKind kind, params int[] segmentPageCounts)
    {
        var inboxRoot = InboxRoot(kind);
        var segments = new List<ChapterSegment>();
        var marker = 0;
        foreach (var count in segmentPageCounts)
        {
            var files = new List<PageFile>();
            for (var i = 0; i < count; i++)
            {
                var src = Path.Combine(inboxRoot, $"src-{baseName}-{marker}.jpg");
                await File.WriteAllBytesAsync(src, [0xFF, 0xD8, (byte)marker]);
                files.Add(new PageFile(i, $"{i}.jpg", src));
                marker++;
            }

            segments.Add(new ChapterSegment("1", null, null, "en", null, files));
        }

        await new CbzChapterWriter(TestPageEncoding.Resolver).WriteAsync(
            new WriteRequest("x", [], [], StorageFormat.Cbz, inboxRoot, baseName, segments));

        foreach (var tmp in Directory.EnumerateFiles(inboxRoot, $"src-{baseName}-*.jpg"))
        {
            File.Delete(tmp); // keep only the .cbz in the inbox
        }

        return baseName + ".cbz";
    }

    private static LocalSeriesMetadata Meta(string title) =>
        new(title, null, ["Some Author"], ["Action"], "A manual series", "Safe", "Completed", 2021, "ja", null);

    [Fact]
    public async Task Create_series_applies_metadata_and_marks_it_local()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var id = await NewService(db).CreateSeriesAsync(Meta("Manual One"));

        var series = await db.Series.Include(s => s.SourceLinks).SingleAsync(s => s.Id == id);
        Assert.Equal("Manual One", series.Title);
        Assert.Equal(ContentRating.Safe, series.ContentRating);
        Assert.Equal(PublicationStatus.Completed, series.Status);
        Assert.Equal(2021, series.Year);
        Assert.Contains(series.SourceLinks, l => l.SourceId == LocalSourceConstants.SourceId && l.IsMetadataPrimary);
    }

    /// <summary>Manga and comics live under separate roots, so a comic's files must land under the comic
    /// root and nothing may appear under the manga one. This is the regression that catches a resolve site
    /// that was never handed the kind — it would otherwise write (or look) in the wrong library silently.</summary>
    [Fact]
    public async Task A_comics_files_land_under_the_comic_root_and_read_back_from_it()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = NewService(db);

        var fileName = await WriteInboxCbzAsync("comic-import", MediaKind.Comic, 3);
        var seriesId = await svc.CreateSeriesAsync(Meta("The Sandman") with { Kind = MediaKind.Comic });

        await svc.ImportAsync(seriesId, new LocalImportRequest(fileName, "en", [new LocalChapterSpec("1", null, null, 0)]));

        var series = await db.Series.Include(s => s.Artifacts).SingleAsync(s => s.Id == seriesId);
        Assert.Equal(MediaKind.Comic, series.Kind);

        var artifact = Assert.Single(series.Artifacts);
        var absolute = _paths.Absolute(MediaKind.Comic, artifact.Path);
        Assert.True(File.Exists(absolute), $"expected the artifact under the comic root, at {absolute}");
        Assert.StartsWith(_paths.Root(MediaKind.Comic), absolute, StringComparison.Ordinal);

        // Nothing may have leaked into the other library's tree.
        Assert.Empty(Directory.EnumerateFileSystemEntries(_paths.Root(MediaKind.Manga)));

        // And the reader resolves it — the path round-trips through the comic root, not the manga one.
        var chapter = await db.Chapters.SingleAsync(c => c.SeriesId == seriesId);
        var page = await NewReader(db).OpenPageAsync(chapter.Id, 0);
        Assert.NotNull(page);
    }

    [Fact]
    public async Task Two_series_sharing_a_tag_name_reuse_the_same_tag_row()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = NewService(db);

        var id1 = await svc.CreateSeriesAsync(Meta("Manual One"));
        var id2 = await svc.CreateSeriesAsync(Meta("Manual Two"));

        var tags = await db.Tags.Where(t => t.Name == "Action").ToListAsync();
        var tag = Assert.Single(tags);
        Assert.Equal("other", tag.Group); // no group hint for free-typed local tags

        var s1 = await db.Series.Include(s => s.Tags).SingleAsync(s => s.Id == id1);
        var s2 = await db.Series.Include(s => s.Tags).SingleAsync(s => s.Id == id2);
        Assert.Equal(tag.Id, Assert.Single(s1.Tags).Id);
        Assert.Equal(tag.Id, Assert.Single(s2.Tags).Id);
    }

    [Fact]
    public async Task Import_single_chapter_is_readable()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = NewService(db);

        var id = await svc.CreateSeriesAsync(Meta("Single"));
        var file = await WriteInboxCbzAsync("single", 3);

        var count = await svc.ImportAsync(id, new LocalImportRequest(file, "en", [new LocalChapterSpec("1", null, null, 0)]));
        Assert.Equal(1, count);

        var chapter = await db.Chapters.SingleAsync(c => c.SeriesId == id);
        var artifact = await db.Artifacts.SingleAsync(a => a.SeriesId == id);
        Assert.Equal(ArtifactOrigin.Local, artifact.Origin);
        Assert.Equal(3, artifact.PageCount);
        Assert.Equal(artifact.Id, chapter.ActiveArtifactId);

        var manifest = await NewReader(db).GetManifestAsync(Guid.NewGuid(), chapter.Id);
        Assert.Equal(3, manifest!.PageCount);
    }

    [Fact]
    public async Task Import_multi_chapter_windows_each_chapter()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = NewService(db);

        var id = await svc.CreateSeriesAsync(Meta("Volume"));
        var file = await WriteInboxCbzAsync("vol", 5); // one 5-page file, split into 2 + 3

        var count = await svc.ImportAsync(id, new LocalImportRequest(file, "en",
            [new LocalChapterSpec("1", null, null, 2), new LocalChapterSpec("2", null, null, 3)]));
        Assert.Equal(2, count);

        var chapters = await db.Chapters.Where(c => c.SeriesId == id).OrderBy(c => c.NumberSort).ToListAsync();
        Assert.Equal(2, chapters.Count);
        Assert.Single(await db.Artifacts.Where(a => a.SeriesId == id).ToListAsync()); // one shared file

        var reader = NewReader(db);
        var second = await reader.GetManifestAsync(Guid.NewGuid(), chapters[1].Id);
        Assert.Equal(3, second!.PageCount); // chapter 2 is its own 3 pages, not the whole 5

        // Chapter 2, page 0 is the file's global page index 2 (marker byte 2).
        var page = await reader.OpenPageAsync(chapters[1].Id, 0);
        using var ms = new MemoryStream();
        await page!.Stream.CopyToAsync(ms);
        await page.Stream.DisposeAsync();
        Assert.Equal([0xFF, 0xD8, 2], ms.ToArray());
    }

    [Fact]
    public async Task Import_rejects_page_counts_that_do_not_sum()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var svc = NewService(db);

        var id = await svc.CreateSeriesAsync(Meta("Bad"));
        var file = await WriteInboxCbzAsync("bad", 4);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ImportAsync(
            id, new LocalImportRequest(file, "en", [new LocalChapterSpec("1", null, null, 2), new LocalChapterSpec("2", null, null, 5)])));
    }

    [Fact]
    public async Task Inbox_listing_reports_page_counts()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await WriteInboxCbzAsync("listme", 7);

        var items = await NewService(db).ListInboxAsync(MediaKind.Manga);
        var cbz = Assert.Single(items, i => i.Name == "listme.cbz");
        Assert.Equal("cbz", cbz.Kind);
        Assert.Equal(7, cbz.PageCount);
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
