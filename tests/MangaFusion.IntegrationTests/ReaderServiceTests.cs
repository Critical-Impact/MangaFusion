using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.IntegrationTests;

public class ReaderServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-reader-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-reader-lib-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    public ReaderServiceTests()
    {
        Directory.CreateDirectory(_root);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _root })
            .Build();
        _paths = new LibraryPaths(config);
    }

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private ReaderService NewService(AppDbContext db) =>
        new(db, new ArtifactReaderRegistry([new CbzArtifactReader(), new FolderArtifactReader(_paths)]), _paths);

    // ReadingProgress FKs to AspNetUsers, so progress-writing tests need real user rows.
    private async Task SeedUsersAsync(AppDbContext db)
    {
        db.Users.AddRange(
            new ApplicationUser { Id = _userA, UserName = "a", NormalizedUserName = "A", Email = "a@x", NormalizedEmail = "A@X", SecurityStamp = "s" },
            new ApplicationUser { Id = _userB, UserName = "b", NormalizedUserName = "B", Email = "b@x", NormalizedEmail = "B@X", SecurityStamp = "s" });
        await db.SaveChangesAsync();
    }

    /// <summary>Writes a real CBZ (one segment per page-count) into the given library's root, and returns
    /// its path relative to that root (which is how Artifact.Path is stored) + total pages.</summary>
    private async Task<(string RelPath, int Pages)> WriteCbzAsync(
        string baseName, MediaKind kind, params int[] segmentPageCounts)
    {
        var segments = new List<ChapterSegment>();
        var marker = 0;
        foreach (var count in segmentPageCounts)
        {
            var files = new List<PageFile>();
            for (var i = 0; i < count; i++)
            {
                var src = Path.Combine(_root, $"src-{baseName}-{marker}.jpg");
                await File.WriteAllBytesAsync(src, [0xFF, 0xD8, (byte)marker]);
                files.Add(new PageFile(i, $"{i}.jpg", src));
                marker++;
            }

            segments.Add(new ChapterSegment("1", null, null, "en", null, files));
        }

        var result = await new CbzChapterWriter(TestPageEncoding.Resolver).WriteAsync(
            new WriteRequest("S", [], [], StorageFormat.Cbz, _paths.SeriesDirectory(kind, "S"), baseName, segments));
        return (_paths.RelativeTo(kind, result.Path), result.PageCount);
    }

    private static Series NewSeries(string? originalLanguage = "en", MediaKind kind = MediaKind.Manga)
    {
        var s = new Series { Title = "S", OriginalLanguage = originalLanguage, Kind = kind };

        // SourceSeriesId must be unique per series — SeriesSourceLinks has a unique index on
        // (SourceId, SourceSeriesId), so a fixed literal makes any test that seeds two series fail.
        s.SourceLinks.Add(new SeriesSourceLink
        {
            SourceId = "fake",
            SourceSeriesId = s.Id.ToString("N"),
            IsMetadataPrimary = true,
        });
        return s;
    }

    private static Chapter NewChapter(string number, decimal sort) => new()
    {
        Language = "en",
        Number = number,
        NumberSort = sort,
        NumberKey = number,
    };

    /// <summary>Seeds a single-chapter downloaded artifact and wires the chapter's active pointer.</summary>
    private async Task<(Guid SeriesId, Guid ChapterId)> SeedDownloadedChapterAsync(
        AppDbContext db, int pages, MediaKind kind = MediaKind.Manga)
    {
        var series = NewSeries(kind: kind);
        var chapter = NewChapter("1", 1m);
        series.Chapters.Add(chapter);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var (rel, count) = await WriteCbzAsync($"ch-{chapter.Id:N}", kind, pages);
        var artifact = new Artifact
        {
            SeriesId = series.Id,
            Format = StorageFormat.Cbz,
            Path = rel,
            PageCount = count,
            Hash = "hash-" + chapter.Id.ToString("N"),
            Status = ArtifactStatus.Complete,
        };
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });
        db.Artifacts.Add(artifact);
        chapter.ActiveArtifactId = artifact.Id;
        await db.SaveChangesAsync();

        return (series.Id, chapter.Id);
    }

    /// <summary>Adds another downloaded chapter to an existing series.</summary>
    private async Task<Guid> AddDownloadedChapterAsync(AppDbContext db, Guid seriesId, string number, decimal sort, int pages)
    {
        var chapter = NewChapter(number, sort);
        chapter.SeriesId = seriesId;
        db.Chapters.Add(chapter);
        await db.SaveChangesAsync();

        var (rel, count) = await WriteCbzAsync($"ch-{chapter.Id:N}", MediaKind.Manga, pages);
        var artifact = new Artifact
        {
            SeriesId = seriesId, Format = StorageFormat.Cbz, Path = rel, PageCount = count,
            Hash = "hash-" + chapter.Id.ToString("N"), Status = ArtifactStatus.Complete,
        };
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });
        db.Artifacts.Add(artifact);
        chapter.ActiveArtifactId = artifact.Id;
        await db.SaveChangesAsync();
        return chapter.Id;
    }

    [Fact]
    public async Task Manifest_reports_pages_and_resume_point()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedUsersAsync(db);
        var (_, chapterId) = await SeedDownloadedChapterAsync(db, pages: 5);
        var svc = NewService(db);

        await svc.SaveProgressAsync(_userA, chapterId, 2, completed: false, default);
        var manifest = await svc.GetManifestAsync(_userA, chapterId, default);

        Assert.NotNull(manifest);
        Assert.Equal(5, manifest!.PageCount);
        Assert.Equal(2, manifest.StartPageIndex);
        Assert.Equal("ltr", manifest.ReadingDirection);
    }

    [Fact]
    public async Task Manifest_is_null_when_chapter_not_downloaded()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = NewSeries();
        var chapter = NewChapter("1", 1m);
        series.Chapters.Add(chapter);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        Assert.Null(await NewService(db).GetManifestAsync(_userA, chapter.Id, default));
    }

    [Fact]
    public async Task Manifest_direction_is_rtl_for_japanese()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = NewSeries(originalLanguage: "ja");
        var chapter = NewChapter("1", 1m);
        series.Chapters.Add(chapter);
        db.Series.Add(series);
        await db.SaveChangesAsync();
        var (rel, count) = await WriteCbzAsync($"ja-{chapter.Id:N}", MediaKind.Manga, 2);
        var artifact = new Artifact { SeriesId = series.Id, Format = StorageFormat.Cbz, Path = rel, PageCount = count, Hash = "h" };
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });
        db.Artifacts.Add(artifact);
        chapter.ActiveArtifactId = artifact.Id;
        await db.SaveChangesAsync();

        var manifest = await NewService(db).GetManifestAsync(_userA, chapter.Id, default);
        Assert.Equal("rtl", manifest!.ReadingDirection);
    }

    /// <summary>Right-to-left is a property of manga, not of Japanese. A comic reads left-to-right even if
    /// its OriginalLanguage happens to be one that would flip a manga — otherwise a translated comic, or
    /// one whose language field is simply wrong, would open paging backwards.</summary>
    [Fact]
    public async Task Manifest_direction_is_always_ltr_for_a_comic()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = NewSeries(originalLanguage: "ja", kind: MediaKind.Comic);
        var chapter = NewChapter("1", 1m);
        series.Chapters.Add(chapter);
        db.Series.Add(series);
        await db.SaveChangesAsync();
        var (rel, count) = await WriteCbzAsync($"comic-{chapter.Id:N}", MediaKind.Comic, 2);
        var artifact = new Artifact { SeriesId = series.Id, Format = StorageFormat.Cbz, Path = rel, PageCount = count, Hash = "h" };
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });
        db.Artifacts.Add(artifact);
        chapter.ActiveArtifactId = artifact.Id;
        await db.SaveChangesAsync();

        var manifest = await NewService(db).GetManifestAsync(_userA, chapter.Id, default);
        Assert.Equal("ltr", manifest!.ReadingDirection);
    }

    [Fact]
    public async Task OpenPage_streams_bytes_in_range_and_null_out_of_range()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var (_, chapterId) = await SeedDownloadedChapterAsync(db, pages: 3);
        var svc = NewService(db);

        var page = await svc.OpenPageAsync(chapterId, 1, default);
        Assert.NotNull(page);
        using var ms = new MemoryStream();
        await page!.Stream!.CopyToAsync(ms);
        await page.Stream.DisposeAsync();
        Assert.Equal([0xFF, 0xD8, 1], ms.ToArray()); // second page marker
        Assert.Contains(":1", page.ETag);

        Assert.Null(await svc.OpenPageAsync(chapterId, 3, default));
        Assert.Null(await svc.OpenPageAsync(chapterId, -1, default));
    }

    [Fact]
    public async Task OpenPage_with_matching_If_None_Match_skips_the_archive()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var (_, chapterId) = await SeedDownloadedChapterAsync(db, pages: 3);
        var svc = NewService(db);

        var fresh = await svc.OpenPageAsync(chapterId, 1, default);
        await fresh!.Stream!.DisposeAsync();

        var cached = await svc.OpenPageAsync(chapterId, 1, ifNoneMatch: fresh.ETag, ct: default);
        Assert.NotNull(cached);
        Assert.True(cached!.NotModified);
        Assert.Null(cached.Stream);
        Assert.Null(cached.ContentType);
        Assert.Equal(fresh.ETag, cached.ETag);
    }

    [Fact]
    public async Task SaveProgress_upserts_clamps_and_autocompletes()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedUsersAsync(db);
        var (_, chapterId) = await SeedDownloadedChapterAsync(db, pages: 4);
        var svc = NewService(db);

        await svc.SaveProgressAsync(_userA, chapterId, 1, completed: false, default);
        await svc.SaveProgressAsync(_userA, chapterId, 99, completed: false, default); // beyond range

        var row = await db.ReadingProgress.SingleAsync(p => p.UserId == _userA && p.ChapterId == chapterId);
        Assert.Equal(3, row.PageIndex);   // clamped to last page
        Assert.True(row.Completed);        // reaching the last page auto-completes
    }

    [Fact]
    public async Task Neighbors_are_downloaded_chapters_in_number_order()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = NewSeries();
        var c1 = NewChapter("1", 1m);
        var c2 = NewChapter("2", 2m);
        var c3 = NewChapter("3", 3m);
        var c4 = NewChapter("4", 4m); // not downloaded
        series.Chapters.AddRange([c1, c2, c3, c4]);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        foreach (var c in new[] { c1, c2, c3 })
        {
            var (rel, count) = await WriteCbzAsync($"n-{c.Id:N}", MediaKind.Manga, 1);
            var a = new Artifact { SeriesId = series.Id, Format = StorageFormat.Cbz, Path = rel, PageCount = count, Hash = "h" };
            a.ChapterLinks.Add(new ArtifactChapter { ChapterId = c.Id, Order = 0 });
            db.Artifacts.Add(a);
            c.ActiveArtifactId = a.Id;
        }

        await db.SaveChangesAsync();
        var svc = NewService(db);

        var mid = await svc.GetNeighborsAsync(c2.Id, default);
        Assert.Equal(c1.Id, mid.PrevChapterId);
        Assert.Equal(c3.Id, mid.NextChapterId);

        var last = await svc.GetNeighborsAsync(c3.Id, default);
        Assert.Equal(c2.Id, last.PrevChapterId);
        Assert.Null(last.NextChapterId); // c4 is not downloaded, so it is skipped
    }

    [Fact]
    public async Task ContinueReading_shows_next_chapter_and_is_per_user()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedUsersAsync(db);
        var (seriesId, ch1) = await SeedDownloadedChapterAsync(db, pages: 5);
        var ch2 = await AddDownloadedChapterAsync(db, seriesId, "2", 2m, pages: 4);
        var svc = NewService(db);

        // Nothing read and nothing added -> not a candidate.
        Assert.Empty(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));

        // Reading ch1 (even at page 0) surfaces the series at its current chapter.
        await svc.SaveProgressAsync(_userA, ch1, 2, completed: false, default);
        var started = Assert.Single(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
        Assert.Equal(ch1, started.ChapterId);
        Assert.Equal(2, started.PageIndex);

        // Independent per user.
        Assert.Empty(await svc.GetContinueReadingAsync(_userB, MediaKind.Manga, 12, default));

        // Finishing ch1 advances the rail to the next unread chapter (ch2, at page 0).
        await svc.SaveProgressAsync(_userA, ch1, 4, completed: true, default);
        var next = Assert.Single(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
        Assert.Equal(ch2, next.ChapterId);
        Assert.Equal(0, next.PageIndex);

        // Finishing ch2 too -> caught up -> off the rail.
        await svc.SaveProgressAsync(_userA, ch2, 3, completed: true, default);
        Assert.Empty(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
    }

    /// <summary>Reading progress is per-user, but the series it points at belongs to exactly one library.
    /// The Continue Reading rail used to ignore the kind entirely — so a manga you were part-way through
    /// showed up on the ComicFusion home page. Passing null is the opt-in combined view.</summary>
    [Fact]
    public async Task ContinueReading_is_scoped_to_one_library_unless_asked_for_both()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedUsersAsync(db);

        var (_, mangaChapter) = await SeedDownloadedChapterAsync(db, pages: 5);
        var (_, comicChapter) = await SeedDownloadedChapterAsync(db, pages: 5, kind: MediaKind.Comic);
        var svc = NewService(db);

        await svc.SaveProgressAsync(_userA, mangaChapter, 2, completed: false, default);
        await svc.SaveProgressAsync(_userA, comicChapter, 1, completed: false, default);

        var manga = Assert.Single(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
        Assert.Equal(mangaChapter, manga.ChapterId);

        var comic = Assert.Single(await svc.GetContinueReadingAsync(_userA, MediaKind.Comic, 12, default));
        Assert.Equal(comicChapter, comic.ChapterId);

        // Null kind = the user opted into a Home that spans both libraries.
        var both = await svc.GetContinueReadingAsync(_userA, kind: null, 12, default);
        Assert.Equal(2, both.Count);
    }

    [Fact]
    public async Task Add_as_reading_and_dismiss_toggle_the_rail()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        await SeedUsersAsync(db);
        var (seriesId, ch1) = await SeedDownloadedChapterAsync(db, pages: 5);
        var svc = NewService(db);

        // A series with a downloaded chapter but no progress: not on the rail, not "reading".
        Assert.False(await svc.IsReadingAsync(_userA, seriesId, default));
        Assert.Empty(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));

        // Explicitly add as reading -> appears at the first unread chapter.
        await svc.SetReadingAsync(_userA, seriesId, dismissed: false, default);
        Assert.True(await svc.IsReadingAsync(_userA, seriesId, default));
        var item = Assert.Single(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
        Assert.Equal(ch1, item.ChapterId);

        // Reading it, then dismissing, hides it even though progress exists.
        await svc.SaveProgressAsync(_userA, ch1, 2, completed: false, default);
        await svc.SetReadingAsync(_userA, seriesId, dismissed: true, default);
        Assert.False(await svc.IsReadingAsync(_userA, seriesId, default));
        Assert.Empty(await svc.GetContinueReadingAsync(_userA, MediaKind.Manga, 12, default));
    }

    [Fact]
    public async Task Multi_chapter_artifact_windows_each_chapter_to_its_page_range()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var series = NewSeries();
        var a = NewChapter("1", 1m);
        var b = NewChapter("2", 2m);
        series.Chapters.AddRange([a, b]);
        db.Series.Add(series);
        await db.SaveChangesAsync();

        // Each chapter's active release records its own page count (2 then 3).
        var relA = new ChapterRelease { ChapterId = a.Id, SourceId = "fake", SourceChapterId = "a", PageCount = 2 };
        var relB = new ChapterRelease { ChapterId = b.Id, SourceId = "fake", SourceChapterId = "b", PageCount = 3 };
        db.ChapterReleases.AddRange(relA, relB);
        await db.SaveChangesAsync();
        a.ActiveReleaseId = relA.Id;
        b.ActiveReleaseId = relB.Id;

        // One artifact holding both chapters: 2 + 3 = 5 pages.
        var (rel, count) = await WriteCbzAsync("vol", MediaKind.Manga, 2, 3);
        var artifact = new Artifact { SeriesId = series.Id, Format = StorageFormat.Cbz, Path = rel, PageCount = count, Hash = "vh" };
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = a.Id, Order = 0 });
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = b.Id, Order = 1 });
        db.Artifacts.Add(artifact);
        a.ActiveArtifactId = artifact.Id;
        b.ActiveArtifactId = artifact.Id;
        await db.SaveChangesAsync();

        var svc = NewService(db);

        var manifestB = await svc.GetManifestAsync(_userA, b.Id, default);
        Assert.Equal(3, manifestB!.PageCount); // chapter B is its own 3 pages, not the whole 5

        // Chapter B page 0 is the artifact's global page index 2 (marker byte 2).
        var page = await svc.OpenPageAsync(b.Id, 0, default);
        using var ms = new MemoryStream();
        await page!.Stream!.CopyToAsync(ms);
        await page.Stream.DisposeAsync();
        Assert.Equal([0xFF, 0xD8, 2], ms.ToArray());
        Assert.Contains(":2", page.ETag); // global index 2

        // B page 3 is out of B's window even though the artifact has 5 pages.
        Assert.Null(await svc.OpenPageAsync(b.Id, 3, default));
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
