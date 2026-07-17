using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MangaFusion.UnitTests.Library;

/// <summary>The auto cover is a 2×2 mosaic of member covers that must degrade gracefully when a
/// collection has fewer than four (or zero) covers, and the custom-cover path must reject anything
/// that isn't a real image rather than storing garbage.</summary>
public class CollectionCoverComposerTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"mf-cover-{Guid.NewGuid():N}");
    private readonly LibraryPaths _paths;
    private readonly CollectionCoverComposer _composer;

    public CollectionCoverComposerTests()
    {
        _paths = new LibraryPaths(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _base })
            .Build());
        _composer = new CollectionCoverComposer(_paths, NullLogger<CollectionCoverComposer>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Compose_returns_null_when_there_are_no_covers()
    {
        var result = await _composer.ComposeAsync(MediaKind.Manga, Guid.NewGuid(), [], CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Compose_skips_missing_files()
    {
        var missing = Path.Combine(_base, "nope.jpg");

        var result = await _composer.ComposeAsync(MediaKind.Manga, Guid.NewGuid(), [missing], CancellationToken.None);

        Assert.Null(result);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Compose_writes_a_cover_for_any_number_of_members(int count)
    {
        var id = Guid.NewGuid();
        var files = Enumerable.Range(0, count).Select(i => WriteImage($"src{i}.jpg")).ToList();

        var relative = await _composer.ComposeAsync(MediaKind.Manga, id, files, CancellationToken.None);

        Assert.NotNull(relative);
        var absolute = _paths.Absolute(MediaKind.Manga, relative!);
        Assert.True(File.Exists(absolute));
        // Sanity: it's a decodable 2:3 image at the expected canvas size.
        using var img = Image.Load(absolute);
        Assert.Equal(512, img.Width);
        Assert.Equal(768, img.Height);
    }

    [Fact]
    public async Task StoreCustom_rejects_non_image_data()
    {
        using var garbage = new MemoryStream("not an image"u8.ToArray());

        var result = await _composer.StoreCustomAsync(MediaKind.Manga, Guid.NewGuid(), garbage, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreCustom_saves_a_valid_image()
    {
        var id = Guid.NewGuid();
        using var stream = new MemoryStream();
        using (var img = new Image<Rgba32>(20, 30))
        {
            img.SaveAsPng(stream);
        }
        stream.Position = 0;

        var relative = await _composer.StoreCustomAsync(MediaKind.Manga, id, stream, CancellationToken.None);

        Assert.NotNull(relative);
        Assert.True(File.Exists(_paths.Absolute(MediaKind.Manga, relative!)));
    }

    private string WriteImage(string name, int w = 16, int h = 24)
    {
        var dir = Path.Combine(_base, "src");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        using var img = new Image<Rgba32>(w, h);
        img.SaveAsJpeg(path);
        return path;
    }
}
