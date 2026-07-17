using System.IO.Compression;
using System.Text;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Writing;

public class ArtifactPageReencoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-reencoder-{Guid.NewGuid():N}");

    public ArtifactPageReencoderTests() => Directory.CreateDirectory(_dir);

    private sealed class StubEncoder(Func<string, EncodedPage?> respond) : IPageImageEncoder
    {
        public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
            Task.FromResult(respond(sourcePath));
    }

    private ArtifactPageReencoder Reencoder(IPageImageEncoder encoder)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Library:RootPath"] = Path.Combine(_dir, "library"),
                ["Library:TempPath"] = Path.Combine(_dir, "tmp"),
            })
            .Build();
        var paths = new LibraryPaths(config);
        var resolver = new PageEncodingResolver(encoder, NullLogger<PageEncodingResolver>.Instance);
        return new ArtifactPageReencoder(resolver, paths);
    }

    private string WriteFixtureCbz()
    {
        var path = Path.Combine(_dir, "fixture.cbz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(zip, "001.jpg", [0xAA, 0xBB, 0xCC]);
        WriteEntry(zip, "002.jpg", [0xDD, 0xEE, 0xFF]);
        WriteEntry(zip, "ComicInfo.xml", Encoding.UTF8.GetBytes("<ComicInfo/>"));
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        stream.Write(bytes);
    }

    private string WriteFixtureFolder()
    {
        var dir = Path.Combine(_dir, "fixture-folder");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "001.jpg"), [0xAA, 0xBB, 0xCC]);
        File.WriteAllBytes(Path.Combine(dir, "002.jpg"), [0xDD, 0xEE, 0xFF]);
        File.WriteAllText(Path.Combine(dir, "ComicInfo.xml"), "<ComicInfo/>");
        return dir;
    }

    [Fact]
    public async Task Cbz_reencodes_image_entries_when_encoder_accepts()
    {
        var path = WriteFixtureCbz();
        var reencoder = Reencoder(new StubEncoder(_ => new EncodedPage([0x01], ".webp")));

        await reencoder.ReencodeAsync(path, StorageFormat.Cbz, CancellationToken.None);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Contains("001.webp", names);
        Assert.Contains("002.webp", names);
        Assert.DoesNotContain("001.jpg", names);
        Assert.DoesNotContain("002.jpg", names);

        var comicInfo = zip.GetEntry("ComicInfo.xml")!;
        using var ms = new MemoryStream();
        comicInfo.Open().CopyTo(ms);
        Assert.Equal("<ComicInfo/>", Encoding.UTF8.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task Cbz_leaves_entries_untouched_when_encoder_declines()
    {
        var path = WriteFixtureCbz();
        var reencoder = Reencoder(new StubEncoder(_ => null));

        await reencoder.ReencodeAsync(path, StorageFormat.Cbz, CancellationToken.None);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.Name).OrderBy(n => n).ToList();
        Assert.Equal(["001.jpg", "002.jpg", "ComicInfo.xml"], names);

        var entry = zip.GetEntry("001.jpg")!;
        using var ms = new MemoryStream();
        entry.Open().CopyTo(ms);
        Assert.Equal([0xAA, 0xBB, 0xCC], ms.ToArray());
    }

    [Fact]
    public async Task Folder_reencodes_image_files_when_encoder_accepts()
    {
        var dir = WriteFixtureFolder();
        var reencoder = Reencoder(new StubEncoder(_ => new EncodedPage([0x01], ".webp")));

        await reencoder.ReencodeAsync(dir, StorageFormat.Folder, CancellationToken.None);

        var names = Directory.EnumerateFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Contains("001.webp", names);
        Assert.Contains("002.webp", names);
        Assert.DoesNotContain("001.jpg", names);
        Assert.Equal("<ComicInfo/>", File.ReadAllText(Path.Combine(dir, "ComicInfo.xml")));
    }

    [Fact]
    public async Task Folder_leaves_files_untouched_when_encoder_declines()
    {
        var dir = WriteFixtureFolder();
        var reencoder = Reencoder(new StubEncoder(_ => null));

        await reencoder.ReencodeAsync(dir, StorageFormat.Folder, CancellationToken.None);

        var names = Directory.EnumerateFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToList();
        Assert.Equal(["001.jpg", "002.jpg", "ComicInfo.xml"], names);
        Assert.Equal([0xAA, 0xBB, 0xCC], File.ReadAllBytes(Path.Combine(dir, "001.jpg")));
    }

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
