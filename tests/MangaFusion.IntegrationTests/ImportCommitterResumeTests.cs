using System.IO.Compression;
using MangaFusion.Application.Library;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

/// <summary>A commit is not atomic: each item is a file write into the library plus an irreversible delete
/// from the inbox. So a commit that dies half-way is a normal, reachable state, and the wizard deliberately
/// puts such a series back into NeedsReview so the user can just retry. These tests pin down what that retry
/// must do — resume, not restart.</summary>
public class ImportCommitterResumeTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-icr-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-icr-lib-{Guid.NewGuid():N}");
    private readonly string _inbox = Path.Combine(Path.GetTempPath(), $"mf-icr-inbox-{Guid.NewGuid():N}");
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"mf-icr-tmp-{Guid.NewGuid():N}");
    private readonly IConfiguration _config;
    private readonly LibraryPaths _paths;
    private readonly ImportPaths _importPaths;

    private const string Folder = "Berserk (2000)";

    public ImportCommitterResumeTests()
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
            db, _paths, writers, new ArtifactFileInspector(), new PdfPageExtractor(_config));

        // The unmatched-import path (no source match, no merge target) never reaches ILibraryService — and
        // it's the path that matters here, because it's the one that *creates* a series, so it's where a
        // non-idempotent retry would leave an orphan behind.
        return new ImportCommitter(
            db, null!, _importPaths, chapterImporter, new NullNotifier(),
            NullLogger<ImportCommitter>.Instance);
    }

    /// <summary>A CBZ with one page; <paramref name="pages"/> 0 makes it unreadable, which is what fails the
    /// item mid-commit (ChapterFileImporter rejects a source with no page images).</summary>
    private void StageCbz(string fileName, int pages)
    {
        var dir = _importPaths.SeriesInboxFolder(MediaKind.Manga, Folder);
        Directory.CreateDirectory(dir);
        using var zip = ZipFile.Open(Path.Combine(dir, fileName), ZipArchiveMode.Create);
        for (var i = 0; i < pages; i++)
        {
            using var entry = zip.CreateEntry($"{i:D3}.jpg").Open();
            entry.Write([0xFF, 0xD8, (byte)i]);
        }
    }

    private string InboxFile(string fileName) =>
        Path.Combine(_importPaths.SeriesInboxFolder(MediaKind.Manga, Folder), fileName);

    private async Task<ImportSeries> SeedAsync(AppDbContext db)
    {
        await db.Database.MigrateAsync();

        var batch = new ImportBatch { Kind = MediaKind.Manga, Status = ImportBatchStatus.Done };
        var series = new ImportSeries { Batch = batch, GroupTitle = "Berserk" };
        series.Items.Add(new ImportItem
        {
            FolderName = Folder, FileName = "v1.cbz", Format = ImportSourceFormat.Cbz,
            Number = "1", PageCount = 1,
        });
        series.Items.Add(new ImportItem
        {
            FolderName = Folder, FileName = "v2.cbz", Format = ImportSourceFormat.Cbz,
            Number = "2", PageCount = 1,
        });
        db.ImportBatches.Add(batch);
        db.ImportSeries.Add(series);
        await db.SaveChangesAsync();
        return series;
    }

    private static async Task<ImportSeries> ReloadAsync(AppDbContext db, Guid id) =>
        await db.ImportSeries.Include(s => s.Items).Include(s => s.Batch).FirstAsync(s => s.Id == id);

    /// <summary>The core scenario: item 1 lands, item 2 blows up. The retry must not re-import item 1 (its
    /// source file is gone and its chapter exists — re-importing it would collide with itself, which is
    /// exactly how this used to dead-end), and must not create a second library series.</summary>
    [Fact]
    public async Task A_commit_that_fails_part_way_resumes_instead_of_restarting()
    {
        StageCbz("v1.cbz", pages: 1);
        StageCbz("v2.cbz", pages: 0); // unreadable -> fails mid-loop, after item 1 has already landed

        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        var firstAttempt = await ReloadAsync(db, seeded.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewCommitter(db).CommitAsync(firstAttempt, CancellationToken.None));

        var afterFailure = await ReloadAsync(db, seeded.Id);
        var item1 = afterFailure.Items.Single(i => i.FileName == "v1.cbz");
        var item2 = afterFailure.Items.Single(i => i.FileName == "v2.cbz");

        // Item 1 is durably marked as done, its source consumed; item 2 is untouched and still retryable.
        Assert.NotNull(item1.ImportedAt);
        Assert.Null(item2.ImportedAt);
        Assert.False(File.Exists(InboxFile("v1.cbz")));
        Assert.True(File.Exists(InboxFile("v2.cbz")));

        // The series it resolved to is recorded, so the retry lands in the same one.
        var seriesId = afterFailure.CommittedLibrarySeriesId;
        Assert.NotNull(seriesId);
        Assert.Equal(1, await db.Chapters.CountAsync(c => c.SeriesId == seriesId));

        // The user fixes the bad file and retries.
        File.Delete(InboxFile("v2.cbz"));
        StageCbz("v2.cbz", pages: 1);

        var retry = await ReloadAsync(db, seeded.Id);
        await NewCommitter(db).CommitAsync(retry, CancellationToken.None);

        var afterRetry = await ReloadAsync(db, seeded.Id);
        Assert.Equal(ImportSeriesStatus.Committed, afterRetry.Status);
        Assert.Equal(seriesId, afterRetry.CommittedLibrarySeriesId);
        Assert.All(afterRetry.Items, i => Assert.NotNull(i.ImportedAt));

        // Two chapters, not three: item 1 was resumed past, not imported a second time.
        Assert.Equal(2, await db.Chapters.CountAsync(c => c.SeriesId == seriesId));
        Assert.Equal(1, await db.Series.CountAsync()); // and no orphaned second series
    }

    /// <summary>The number-collision pre-check must only look at items still to import. Including the
    /// already-imported ones would make it trip over the very chapters the first attempt created, failing
    /// every retry before it started.</summary>
    [Fact]
    public async Task A_resumed_items_own_chapter_is_not_treated_as_a_collision()
    {
        StageCbz("v1.cbz", pages: 1);
        StageCbz("v2.cbz", pages: 0);

        await using var db = NewContext();
        var seeded = await SeedAsync(db);

        var firstAttempt = await ReloadAsync(db, seeded.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewCommitter(db).CommitAsync(firstAttempt, CancellationToken.None));

        File.Delete(InboxFile("v2.cbz"));
        StageCbz("v2.cbz", pages: 1);

        // Would throw "'1' already exists in this series" if item 1 were still being checked.
        var retry = await ReloadAsync(db, seeded.Id);
        var exception = await Record.ExceptionAsync(
            () => NewCommitter(db).CommitAsync(retry, CancellationToken.None));

        Assert.Null(exception);
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
