using System.IO.Compression;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

/// <summary>The two rules a merge target must obey. See <c>MergeTarget</c>.
///
/// Rule 1, the library. Manga and comics share a database but not a root directory, and a series' files
/// are written under its own kind's root. A manga batch that merges into a comic series does not fail. It
/// writes the chapters into the wrong library, where that library's UI never looks for them.
///
/// Rule 2, the source. A batch is matched against one metadata source. A same-titled series from another
/// source, or one linked to a different id on the same source, is not shown to be the same work.
///
/// Both rules are reachable because a shared title is ordinary: an adaptation keeps the title of its
/// source, and different works do share a title. That is exactly when a merge target is
/// auto-suggested.</summary>
public class MergeTargetTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-mtk-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-mtk-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-mtk-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-mtk-tmp-{Guid.NewGuid():N}");
    private readonly IConfiguration _config;
    private readonly LibraryPaths _paths;
    private readonly ImportPaths _importPaths;

    private const string Folder = "Berserk";

    public MergeTargetTests()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Library:RootPath"] = _root,
                ["Library:TempPath"] = _tempRoot,
                ["Import:InboxPath"] = _inbox,
            })
            .Build();
        _paths = new LibraryPaths(_config);
        _importPaths = new ImportPaths(_config);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private ImportCommitter NewCommitter(AppDbContext db)
    {
        var writers = new ChapterWriterSelector(
            [new CbzChapterWriter(TestPageEncoding.Resolver), new FolderChapterWriter(TestPageEncoding.Resolver)],
            _config);
        var chapterImporter = new ChapterFileImporter(
            db, _paths, writers, new ArtifactFileInspector(), new PdfPageExtractor(_config),
            new CbrPageExtractor(), new EpubPageExtractor());

        var proseImporter = new ProseChapterImporter(db, _paths, new EpubChapterWriter());
        return new ImportCommitter(
            db, null!, _importPaths, chapterImporter, proseImporter, new NullNotifier(), NullLogger<ImportCommitter>.Instance);
    }

    private void StageCbz(string fileName)
    {
        var dir = _importPaths.SeriesInboxFolder(MediaKind.Manga, Folder);
        Directory.CreateDirectory(dir);
        using var zip = ZipFile.Open(Path.Combine(dir, fileName), ZipArchiveMode.Create);
        using var entry = zip.CreateEntry("001.jpg").Open();
        entry.Write([0xFF, 0xD8, 0x01]);
    }

    private string InboxFile(string fileName) =>
        Path.Combine(_importPaths.SeriesInboxFolder(MediaKind.Manga, Folder), fileName);

    /// <summary>A manga batch pointed at a comic series must be refused at commit — and must be refused
    /// <em>before</em> anything moves, so the inbox file is still there to retry with.</summary>
    [Fact]
    public async Task Committing_a_manga_import_into_a_comic_series_is_refused()
    {
        StageCbz("v1.cbz");

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        // Same title in the other library — the case that makes this reachable.
        var comic = new Series { Title = "Berserk", Kind = MediaKind.Comic };
        db.Series.Add(comic);

        var batch = new ImportBatch { Kind = MediaKind.Manga, Status = ImportBatchStatus.Done };
        var importSeries = new ImportSeries
        {
            Batch = batch,
            GroupTitle = "Berserk",
            ExistingLibrarySeriesId = comic.Id, // merge target in the wrong library
        };
        importSeries.Items.Add(new ImportItem
        {
            FolderName = Folder, FileName = "v1.cbz", Format = ImportSourceFormat.Cbz, Number = "1", PageCount = 1,
        });
        db.ImportBatches.Add(batch);
        db.ImportSeries.Add(importSeries);
        await db.SaveChangesAsync();

        var loaded = await db.ImportSeries.Include(s => s.Items).Include(s => s.Batch)
            .FirstAsync(s => s.Id == importSeries.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewCommitter(db).CommitAsync(loaded, CancellationToken.None));

        Assert.Contains("Comic library", ex.Message);

        // Nothing was written into the comic series, and the source file is untouched.
        Assert.Equal(0, await db.Chapters.CountAsync());
        Assert.Equal(0, await db.Artifacts.CountAsync());
        Assert.True(File.Exists(InboxFile("v1.cbz")));
    }

    /// <summary>The control: the same commit into a same-library target goes through, so the guard is
    /// rejecting on kind and not just rejecting merges.</summary>
    [Fact]
    public async Task Committing_into_a_same_library_series_still_works()
    {
        StageCbz("v1.cbz");

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var manga = new Series { Title = "Berserk", Kind = MediaKind.Manga };
        db.Series.Add(manga);

        var batch = new ImportBatch { Kind = MediaKind.Manga, Status = ImportBatchStatus.Done };
        var importSeries = new ImportSeries
        {
            Batch = batch, GroupTitle = "Berserk", ExistingLibrarySeriesId = manga.Id,
        };
        importSeries.Items.Add(new ImportItem
        {
            FolderName = Folder, FileName = "v1.cbz", Format = ImportSourceFormat.Cbz, Number = "1", PageCount = 1,
        });
        db.ImportBatches.Add(batch);
        db.ImportSeries.Add(importSeries);
        await db.SaveChangesAsync();

        var loaded = await db.ImportSeries.Include(s => s.Items).Include(s => s.Batch)
            .FirstAsync(s => s.Id == importSeries.Id);

        var seriesId = await NewCommitter(db).CommitAsync(loaded, CancellationToken.None);

        Assert.Equal(manga.Id, seriesId);
        Assert.Equal(1, await db.Chapters.CountAsync(c => c.SeriesId == manga.Id));
        Assert.False(File.Exists(InboxFile("v1.cbz"))); // consumed
    }

    /// <summary>The reported case: a MangaDex series called "Berserk" is in the library, and a
    /// MangaUpdates-matched import of a different work with the same title points at it. Nothing links the
    /// two, so the commit must refuse it and leave the inbox file to retry with.</summary>
    [Fact]
    public async Task Committing_an_import_into_a_series_from_another_source_is_refused()
    {
        StageCbz("v1.cbz");

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var fromMangaDex = NewSeries("Berserk", ("mangadex", "0e4a1b2c"));
        db.Series.Add(fromMangaDex);
        var loaded = await StageMergeAsync(db, fromMangaDex.Id, matchedSourceSeriesId: "51239621230");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewCommitter(db).CommitAsync(loaded, CancellationToken.None));

        Assert.Contains("mangadex", ex.Message);
        Assert.Equal(0, await db.Chapters.CountAsync());
        Assert.True(File.Exists(InboxFile("v1.cbz")));
    }

    /// <summary>A target already linked to a different id on the batch's own source is a different work,
    /// which is the strongest evidence available. Refuse it.</summary>
    [Fact]
    public async Task Committing_an_import_into_a_different_id_on_the_same_source_is_refused()
    {
        StageCbz("v1.cbz");

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var otherWork = NewSeries("Berserk", ("mangaupdates", "99999999999"));
        db.Series.Add(otherWork);
        var loaded = await StageMergeAsync(db, otherWork.Id, matchedSourceSeriesId: "51239621230");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewCommitter(db).CommitAsync(loaded, CancellationToken.None));

        Assert.Contains("different", ex.Message);
        Assert.Equal(0, await db.Chapters.CountAsync());
        Assert.True(File.Exists(InboxFile("v1.cbz")));
    }

    /// <summary>The control for rule 2: a hand-created local series and a series already on the matched
    /// MangaUpdates id both stay eligible, so the guard rejects on the source and not on merges as
    /// such.</summary>
    [Theory]
    [InlineData("local", "8f2c1d")]
    [InlineData("mangaupdates", "51239621230")]
    public async Task Committing_into_a_local_or_same_id_series_still_works(string sourceId, string sourceSeriesId)
    {
        StageCbz("v1.cbz");

        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var target = NewSeries("Berserk", (sourceId, sourceSeriesId));
        db.Series.Add(target);
        var loaded = await StageMergeAsync(db, target.Id, matchedSourceSeriesId: "51239621230");

        var seriesId = await NewCommitter(db).CommitAsync(loaded, CancellationToken.None);

        Assert.Equal(target.Id, seriesId);
        Assert.Equal(1, await db.Chapters.CountAsync(c => c.SeriesId == target.Id));
        Assert.False(File.Exists(InboxFile("v1.cbz"))); // consumed
    }

    private static Series NewSeries(string title, params (string SourceId, string SourceSeriesId)[] links)
    {
        var series = new Series { Title = title, Kind = MediaKind.Manga };
        foreach (var (sourceId, sourceSeriesId) in links)
        {
            series.SourceLinks.Add(new SeriesSourceLink
            {
                SourceId = sourceId,
                SourceSeriesId = sourceSeriesId,
                Kind = MediaKind.Manga,
                IsMetadataPrimary = sourceId != "local",
            });
        }

        return series;
    }

    /// <summary>One manga import series, one CBZ, pointed at <paramref name="mergeTargetId"/>.</summary>
    private static async Task<ImportSeries> StageMergeAsync(
        AppDbContext db, Guid mergeTargetId, string matchedSourceSeriesId)
    {
        var batch = new ImportBatch { Kind = MediaKind.Manga, Status = ImportBatchStatus.Done };
        var importSeries = new ImportSeries
        {
            Batch = batch,
            GroupTitle = "Berserk",
            MatchedSourceSeriesId = matchedSourceSeriesId,
            ExistingLibrarySeriesId = mergeTargetId,
        };
        importSeries.Items.Add(new ImportItem
        {
            FolderName = Folder, FileName = "v1.cbz", Format = ImportSourceFormat.Cbz, Number = "1", PageCount = 1,
        });
        db.ImportBatches.Add(batch);
        db.ImportSeries.Add(importSeries);
        await db.SaveChangesAsync();

        return await db.ImportSeries.Include(s => s.Items).Include(s => s.Batch)
            .FirstAsync(s => s.Id == importSeries.Id);
    }

    private sealed class NullNotifier : ILibraryNotifier
    {
        public Task DownloadProgressAsync(
            Guid downloadId, Guid? chapterId, DownloadStatus status, int pagesDone, int pagesTotal,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task ImportCommitProgressAsync(
            Guid importSeriesId, string status, int itemsDone, int itemsTotal, int? pageDone, int? pageTotal,
            CancellationToken ct = default) => Task.CompletedTask;

        public Task MigrationCommitProgressAsync(
            Guid migrationSeriesId, string status, int itemsDone, int itemsTotal, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task MigrationBatchCommitProgressAsync(
            Guid batchId, int seriesDone, int seriesTotal, CancellationToken ct = default) => Task.CompletedTask;

        public Task NotificationAsync(Guid userId, string title, string? body, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var dir in new[] { _root, _inbox, _tempRoot })
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }

        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
