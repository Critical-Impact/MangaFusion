using MangaFusion.Application.Writing;
using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Re-encodes a page image to lossless WebP when that's smaller than the source. Lossless,
/// not lossy — most manga pages arrive as already-lossy JPEG, so this avoids stacking a second lossy
/// generation on top; the size win comes from WebP's better lossless entropy coding, concentrated on
/// flatter/line-art-heavy pages rather than uniformly across a series.</summary>
public sealed class WebpPageImageEncoder : IPageImageEncoder
{
    private readonly bool _enabled;
    private readonly int _effort; // 0-6: lossless compression effort (speed/ratio trade-off), not a quality knob

    public WebpPageImageEncoder(IConfiguration config)
    {
        var format = config["Encoding:Format"] ?? "Webp";
        _enabled = (config.GetValue<bool?>("Encoding:Enabled") ?? true)
                   && format.Equals("Webp", StringComparison.OrdinalIgnoreCase);
        _effort = Math.Clamp(config.GetValue<int?>("Encoding:Effort") ?? 4, 0, 6);
    }

    public bool Enabled => _enabled;

    public async Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct)
    {
        if (!_enabled)
        {
            return null;
        }

        if (string.Equals(Path.GetExtension(sourcePath), ".webp", StringComparison.OrdinalIgnoreCase))
        {
            return null; // already WebP — lossless-to-lossless re-encode buys nothing
        }

        try
        {
            using var image = await Image.LoadAsync(sourcePath, ct);
            if (image.Frames.Count > 1)
            {
                return null; // skip animated sources
            }

            using var ms = new MemoryStream();
            var encoder = new WebpEncoder
            {
                FileFormat = WebpFileFormatType.Lossless,
                Method = (WebpEncodingMethod)_effort,
            };
            await image.SaveAsync(ms, encoder, ct);
            return new EncodedPage(ms.ToArray(), ".webp");
        }
        catch
        {
            return null; // corrupt/unsupported input — skip this page, never break the write
        }
    }
}
