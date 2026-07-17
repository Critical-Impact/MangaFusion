using System.IO.Compression;
using System.Xml.Linq;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Writing;

public class CbzChapterWriterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-cbz-{Guid.NewGuid():N}");
    private static readonly PageEncodingResolver Resolver =
        new(new NoOpPageImageEncoder(), NullLogger<PageEncodingResolver>.Instance);

    public CbzChapterWriterTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public async Task Writes_cbz_with_padded_pages_and_comicinfo()
    {
        var pageFiles = new List<PageFile>();
        for (var i = 0; i < 3; i++)
        {
            var src = Path.Combine(_dir, $"src{i}.jpg");
            await File.WriteAllBytesAsync(src, [0xFF, 0xD8, (byte)i]); // dummy jpeg-ish bytes
            pageFiles.Add(new PageFile(i, $"{i}.jpg", src));
        }

        var request = new WriteRequest(
            SeriesTitle: "Test Series",
            Authors: ["Author A"],
            Genres: ["Action"],
            Format: StorageFormat.Cbz,
            TargetDirectory: _dir,
            FileBaseName: "Test Series - Ch. 10 [Group A]",
            Segments: [new ChapterSegment("10", "2", "The Title", "en", "Group A", pageFiles)],
            Artists: ["Artist A"],
            OtherTags: ["Long strip"],
            Description: "A test summary.",
            ContentRating: ContentRating.Suggestive,
            OriginalLanguage: "ja",
            AltTitles: ["Alt Title"]);

        var result = await new CbzChapterWriter(Resolver).WriteAsync(request);

        Assert.True(File.Exists(result.Path));
        Assert.EndsWith(".cbz", result.Path);
        Assert.Equal(3, result.PageCount);
        Assert.NotEmpty(result.Sha256);

        using var zip = ZipFile.OpenRead(result.Path);
        var names = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Contains("001.jpg", names);
        Assert.Contains("002.jpg", names);
        Assert.Contains("003.jpg", names);
        Assert.Contains("ComicInfo.xml", names);

        var comicInfoEntry = zip.GetEntry("ComicInfo.xml")!;
        await using var stream = comicInfoEntry.Open();
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        Assert.Equal("Test Series", doc.Root!.Element("Series")!.Value);
        Assert.Equal("10", doc.Root.Element("Number")!.Value);
        Assert.Equal("3", doc.Root.Element("PageCount")!.Value);
        Assert.Equal("en", doc.Root.Element("LanguageISO")!.Value);
        Assert.Equal("Action", doc.Root.Element("Genre")!.Value);
        Assert.Equal("Long strip", doc.Root.Element("Tags")!.Value);
        Assert.Equal("Artist A", doc.Root.Element("Penciller")!.Value);
        Assert.Equal("Artist A", doc.Root.Element("CoverArtist")!.Value);
        Assert.Equal("A test summary.", doc.Root.Element("Summary")!.Value);
        Assert.Equal("Alternate titles: Alt Title", doc.Root.Element("Notes")!.Value);
        Assert.Equal("Teen", doc.Root.Element("AgeRating")!.Value);
        Assert.Equal("YesAndRightToLeft", doc.Root.Element("Manga")!.Value);
    }

    /// <summary>ComicInfo's Manga element is how Komga/Kavita/Mihon decide page order. A comic must write
    /// "No" — writing "Yes" (or worse, inheriting "YesAndRightToLeft" from a Japanese OriginalLanguage)
    /// makes every downstream reader page the exported file backwards.</summary>
    [Fact]
    public async Task A_comic_writes_Manga_No_whatever_its_original_language_says()
    {
        var src = Path.Combine(_dir, "p1.jpg");
        await File.WriteAllBytesAsync(src, [0xFF, 0xD8]);

        var request = new WriteRequest(
            SeriesTitle: "Batman",
            Authors: ["Scott Snyder"],
            Genres: [],
            Format: StorageFormat.Cbz,
            TargetDirectory: _dir,
            FileBaseName: "Batman - 001",
            Segments: [new ChapterSegment("1", null, "Knife Trick", "en", null, [new PageFile(0, "0.jpg", src)])],
            // Deliberately the language that would otherwise force YesAndRightToLeft.
            OriginalLanguage: "ja",
            Kind: MediaKind.Comic);

        var result = await new CbzChapterWriter(Resolver).WriteAsync(request);

        using var zip = ZipFile.OpenRead(result.Path);
        await using var stream = zip.GetEntry("ComicInfo.xml")!.Open();
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        Assert.Equal("No", doc.Root!.Element("Manga")!.Value);
    }

    [Fact]
    public async Task Multi_segment_write_produces_number_range()
    {
        var segments = new List<ChapterSegment>();
        foreach (var n in new[] { "6", "7" })
        {
            var src = Path.Combine(_dir, $"p{n}.jpg");
            await File.WriteAllBytesAsync(src, [0xFF, 0xD8]);
            segments.Add(new ChapterSegment(n, "1", null, "en", "Group A", [new PageFile(0, "0.jpg", src)]));
        }

        var request = new WriteRequest("S", [], [], StorageFormat.Cbz, _dir, "S - Vol. 1", segments);
        var result = await new CbzChapterWriter(Resolver).WriteAsync(request);

        using var zip = ZipFile.OpenRead(result.Path);
        await using var stream = zip.GetEntry("ComicInfo.xml")!.Open();
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        Assert.Equal("6-7", doc.Root!.Element("Number")!.Value);
        Assert.Equal(2, result.PageCount);
    }

    /// <summary>Two writes with the same base name must not land on the same file. The name comes from
    /// series title + chapter number + group, so this is reachable in normal use: two inbox files with the
    /// same filename in different subfolders, or re-downloading a chapter that's already on disk. The
    /// second write overwriting the first would leave the first artifact's DB row pointing at the second's
    /// bytes — the reader then serves the wrong pages for a chapter that looks perfectly healthy.</summary>
    [Fact]
    public async Task Same_base_name_written_twice_does_not_overwrite_the_first_file()
    {
        var writer = new CbzChapterWriter(Resolver);

        var first = await writer.WriteAsync(RequestWith("Dup", await PageAsync("a", 0xA1)));
        var second = await writer.WriteAsync(RequestWith("Dup", await PageAsync("b", 0xB2)));

        Assert.NotEqual(first.Path, second.Path);
        Assert.True(File.Exists(first.Path));
        Assert.True(File.Exists(second.Path));

        // Distinct content, and — the actual invariant — the first file still holds its own page.
        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.Equal(0xA1, await FirstPageMarkerAsync(first.Path));
        Assert.Equal(0xB2, await FirstPageMarkerAsync(second.Path));
    }

    /// <summary>A failed/cancelled write must not leave its scratch file behind. The temp name is unique per
    /// attempt (so two concurrent writes can't share one), which means nothing would ever reclaim it.</summary>
    [Fact]
    public async Task A_cancelled_write_leaves_no_temp_file_behind()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var request = RequestWith("Cancelled", await PageAsync("c", 0xC3));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new CbzChapterWriter(Resolver).WriteAsync(request, null, cts.Token));

        Assert.Empty(Directory.EnumerateFiles(_dir, "*.tmp"));
        Assert.Empty(Directory.EnumerateFiles(_dir, "*.cbz"));
    }

    private async Task<PageFile> PageAsync(string name, byte marker)
    {
        var src = Path.Combine(_dir, $"{name}.jpg");
        await File.WriteAllBytesAsync(src, [0xFF, 0xD8, marker]);
        return new PageFile(0, $"{name}.jpg", src);
    }

    /// <summary>The marker byte of the archive's first page — identifies which write produced the file.</summary>
    private static async Task<byte> FirstPageMarkerAsync(string cbzPath)
    {
        using var zip = ZipFile.OpenRead(cbzPath);
        var page = zip.Entries.Single(e => e.Name != "ComicInfo.xml");
        await using var stream = page.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray()[2];
    }

    private WriteRequest RequestWith(string baseName, PageFile page) => new(
        SeriesTitle: "Test Series",
        Authors: [],
        Genres: [],
        Format: StorageFormat.Cbz,
        TargetDirectory: _dir,
        FileBaseName: baseName,
        Segments: [new ChapterSegment("1", null, null, "en", null, [page])]);

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
