using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.PixelFormats;

namespace MangaFusion.UnitTests.Writing;

public class WebpPageImageEncoderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-webpenc-{Guid.NewGuid():N}");

    public WebpPageImageEncoderTests() => Directory.CreateDirectory(_dir);

    private static IConfiguration Config(bool enabled = true) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Encoding:Enabled"] = enabled ? "true" : "false" })
            .Build();

    private async Task<string> WriteFlatJpegAsync(string name, int size = 64)
    {
        using var image = new Image<Rgba32>(size, size);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                image[x, y] = new Rgba32(20, 20, 20, 255); // flat color — highly lossless-compressible
            }
        }

        var path = Path.Combine(_dir, name);
        await image.SaveAsJpegAsync(path);
        return path;
    }

    [Fact]
    public async Task Encodes_a_flat_image_to_smaller_lossless_webp()
    {
        var jpegPath = await WriteFlatJpegAsync("flat.jpg");
        var encoder = new WebpPageImageEncoder(Config());

        var result = await encoder.TryEncodeAsync(jpegPath, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(".webp", result!.Extension);
        Assert.True(result.Bytes.Length < new FileInfo(jpegPath).Length);

        // Pixel-exact reconstruction, since lossless.
        using var decoded = await Image.LoadAsync<Rgba32>(new MemoryStream(result.Bytes));
        Assert.Equal(new Rgba32(20, 20, 20, 255), decoded[0, 0]);
    }

    [Fact]
    public async Task Disabled_config_always_declines()
    {
        var jpegPath = await WriteFlatJpegAsync("disabled.jpg");
        var encoder = new WebpPageImageEncoder(Config(enabled: false));

        Assert.Null(await encoder.TryEncodeAsync(jpegPath, CancellationToken.None));
    }

    [Fact]
    public async Task Corrupt_input_returns_null_instead_of_throwing()
    {
        var path = Path.Combine(_dir, "corrupt.jpg");
        await File.WriteAllBytesAsync(path, [0xFF, 0xD8, 0x01, 0x02, 0x03]);
        var encoder = new WebpPageImageEncoder(Config());

        Assert.Null(await encoder.TryEncodeAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task Source_already_webp_is_skipped()
    {
        var jpegPath = await WriteFlatJpegAsync("already.webp"); // content doesn't matter, extension does
        var encoder = new WebpPageImageEncoder(Config());

        Assert.Null(await encoder.TryEncodeAsync(jpegPath, CancellationToken.None));
    }

    [Fact]
    public async Task Animated_source_is_skipped()
    {
        var path = Path.Combine(_dir, "anim.gif");
        using (var image = new Image<Rgba32>(4, 4))
        {
            image.Frames.CreateFrame(); // second frame — makes this an animated GIF
            await image.SaveAsync(path, new GifEncoder());
        }

        var encoder = new WebpPageImageEncoder(Config());
        Assert.Null(await encoder.TryEncodeAsync(path, CancellationToken.None));
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
