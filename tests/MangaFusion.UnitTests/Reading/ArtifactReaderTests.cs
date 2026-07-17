using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;
using MangaFusion.UnitTests.Writing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Reading;

public class ArtifactReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-read-{Guid.NewGuid():N}");
    private static readonly PageEncodingResolver Resolver =
        new(new NoOpPageImageEncoder(), NullLogger<PageEncodingResolver>.Instance);

    public ArtifactReaderTests() => Directory.CreateDirectory(_root);

    private LibraryPaths Paths()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _root })
            .Build();
        return new LibraryPaths(config);
    }

    private async Task<string> WriteCbzAsync(IChapterWriter writer, string baseName, params byte[] pageMarkers)
    {
        var files = new List<PageFile>();
        for (var i = 0; i < pageMarkers.Length; i++)
        {
            var src = Path.Combine(_root, $"src-{baseName}-{i}.jpg");
            await File.WriteAllBytesAsync(src, [0xFF, 0xD8, pageMarkers[i]]);
            files.Add(new PageFile(i, $"{i}.jpg", src));
        }

        // Written inside a real library root: FolderArtifactReader's traversal guard rejects anything that
        // isn't, and the roots are now `_root/manga` and `_root/comics` rather than `_root` itself.
        var request = new WriteRequest(
            "S", [], [], writer.Format, Paths().SeriesDirectory(MediaKind.Manga, "S"), baseName,
            [new ChapterSegment("1", null, null, "en", null, files)]);
        var result = await writer.WriteAsync(request);
        return result.Path;
    }

    [Fact]
    public async Task Cbz_lists_pages_in_order_excluding_comicinfo()
    {
        var path = await WriteCbzAsync(new CbzChapterWriter(Resolver), "cbz", 10, 20, 30);
        var reader = new CbzArtifactReader();

        var pages = await reader.ListPagesAsync(path);

        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.Equal("image/jpeg", p.ContentType));
        Assert.DoesNotContain(pages, p => p.Name.Contains("ComicInfo", StringComparison.OrdinalIgnoreCase));
        Assert.Equal([0, 1, 2], pages.Select(p => p.Index));
    }

    [Fact]
    public async Task Cbz_opens_page_bytes_by_index_and_returns_null_out_of_range()
    {
        var path = await WriteCbzAsync(new CbzChapterWriter(Resolver), "cbz", 10, 20, 30);
        var reader = new CbzArtifactReader();

        var content = await reader.OpenPageAsync(path, 1);
        Assert.NotNull(content);
        using var ms = new MemoryStream();
        await content!.Stream.CopyToAsync(ms);
        await content.Stream.DisposeAsync();
        Assert.Equal([0xFF, 0xD8, 20], ms.ToArray()); // second page's marker byte
        Assert.Equal("image/jpeg", content.ContentType);

        Assert.Null(await reader.OpenPageAsync(path, 99));
        Assert.Null(await reader.OpenPageAsync(path, -1));
    }

    [Fact]
    public async Task Folder_lists_and_opens_pages()
    {
        var path = await WriteCbzAsync(new FolderChapterWriter(Resolver), "folder", 40, 50);
        var reader = new FolderArtifactReader(Paths());

        var pages = await reader.ListPagesAsync(path);
        Assert.Equal(2, pages.Count);
        Assert.DoesNotContain(pages, p => p.Name.Contains("ComicInfo", StringComparison.OrdinalIgnoreCase));

        var content = await reader.OpenPageAsync(path, 0);
        Assert.NotNull(content);
        using var ms = new MemoryStream();
        await content!.Stream.CopyToAsync(ms);
        await content.Stream.DisposeAsync();
        Assert.Equal([0xFF, 0xD8, 40], ms.ToArray());
    }

    [Fact]
    public async Task Folder_reader_rejects_paths_outside_library_root()
    {
        var reader = new FolderArtifactReader(Paths());
        var outside = Path.Combine(Path.GetTempPath(), $"mf-escape-{Guid.NewGuid():N}");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ListPagesAsync(outside));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.OpenPageAsync(outside, 0));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
