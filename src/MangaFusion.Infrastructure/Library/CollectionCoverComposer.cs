using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Builds a collection's cover art with ImageSharp (already a dependency — see
/// <c>Writing/WebpPageImageEncoder.cs</c>): a 2×2 mosaic of member covers for the auto cover, and a
/// normalized JPEG for a custom upload. Best-effort — on any failure it logs and returns null, so the
/// collection simply falls back to a placeholder. Writes into the collection's per-kind cover dir.</summary>
public sealed class CollectionCoverComposer(LibraryPaths paths, ILogger<CollectionCoverComposer> logger)
{
    private const int Width = 512;
    private const int Height = 768; // 2:3 poster aspect, matching series covers

    /// <summary>Composes and writes <c>cover.jpg</c> from the given member cover files (absolute paths,
    /// already in display order). Returns the relative path to persist, or null if nothing composable.</summary>
    public async Task<string?> ComposeAsync(
        MediaKind kind, Guid collectionId, IReadOnlyList<string> coverFiles, CancellationToken ct)
    {
        var available = coverFiles.Where(File.Exists).Take(4).ToList();
        if (available.Count == 0)
        {
            return null;
        }

        try
        {
            using var canvas = new Image<Rgba32>(Width, Height);

            if (available.Count == 1)
            {
                await DrawTileAsync(canvas, available[0], 0, 0, Width, Height, ct);
            }
            else
            {
                var halfW = Width / 2;
                var halfH = Height / 2;
                // Fill all four cells, cycling the covers we have so 2 or 3 members still yield a full
                // balanced mosaic rather than empty corners.
                for (var cell = 0; cell < 4; cell++)
                {
                    var tileFile = available[cell % available.Count];
                    var x = cell % 2 * halfW;
                    var y = cell / 2 * halfH;
                    await DrawTileAsync(canvas, tileFile, x, y, halfW, halfH, ct);
                }
            }

            var dir = paths.CollectionDirectory(kind, collectionId);
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "cover.jpg");
            await canvas.SaveAsync(file, new JpegEncoder { Quality = 82 }, ct);
            return paths.RelativeTo(kind, file);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to compose cover for collection {Collection}", collectionId);
            return null;
        }
    }

    /// <summary>Validates + normalizes an uploaded image to <c>cover-custom.jpg</c> (capped in size so a
    /// huge upload can't bloat storage). Returns the relative path, or null if it isn't a valid image.</summary>
    public async Task<string?> StoreCustomAsync(
        MediaKind kind, Guid collectionId, Stream image, CancellationToken ct)
    {
        var file = await StoreCustomAsync(paths.CollectionDirectory(kind, collectionId), "cover-custom.jpg", image, ct);
        return file is null ? null : paths.RelativeTo(kind, file);
    }

    /// <summary>Directory-scoped core behind the collection-specific overload above — shared with
    /// <see cref="SeriesCoverCache"/> so series cover uploads get the same ImageSharp validation/resize
    /// without duplicating it. Returns the absolute file path written, or null if it isn't a valid
    /// image.</summary>
    public async Task<string?> StoreCustomAsync(string directory, string fileName, Stream image, CancellationToken ct)
    {
        try
        {
            using var img = await Image.LoadAsync(image, ct);
            if (img.Width > 1024 || img.Height > 1536)
            {
                img.Mutate(o => o.Resize(new ResizeOptions { Size = new Size(1024, 1536), Mode = ResizeMode.Max }));
            }

            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, fileName);
            await img.SaveAsync(file, new JpegEncoder { Quality = 85 }, ct);
            return file;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rejected custom cover upload for {Directory}", directory);
            return null;
        }
    }

    private static async Task DrawTileAsync(
        Image<Rgba32> canvas, string file, int x, int y, int w, int h, CancellationToken ct)
    {
        using var tile = await Image.LoadAsync(file, ct);
        tile.Mutate(o => o.Resize(new ResizeOptions { Size = new Size(w, h), Mode = ResizeMode.Crop }));
        canvas.Mutate(o => o.DrawImage(tile, new Point(x, y), 1f));
    }
}
