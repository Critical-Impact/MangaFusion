using MangaFusion.Application.Writing;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>What to actually write for a page: either <c>null</c> bytes (copy the source file as-is,
/// under its own extension) or the encoder's output (write these bytes under the new extension).</summary>
public sealed record ResolvedPage(byte[]? Bytes, string Extension)
{
    public static ResolvedPage Original(string extension) => new(null, extension);

    public static ResolvedPage Encoded(byte[] bytes, string extension) => new(bytes, extension);
}

/// <summary>Centralizes the try/fallback/size-comparison policy around <see cref="IPageImageEncoder"/>
/// once, so every caller (chapter writers, migration re-encoding) shares it instead of duplicating it.
/// An encoder failing, declining, or losing to the original size are all treated identically: keep the
/// original bytes untouched. This guarantee matters especially under lossless encoding, where "the
/// encoded result isn't actually smaller" is a routine outcome, not a rare edge case.</summary>
public sealed class PageEncodingResolver(IPageImageEncoder encoder, ILogger<PageEncodingResolver> logger)
{
    public async Task<ResolvedPage> ResolveAsync(PageFile page, CancellationToken ct)
    {
        var originalExt = Path.GetExtension(page.ArchiveName) is { Length: > 0 } ext ? ext : ".jpg";

        EncodedPage? encoded;
        try
        {
            encoded = await encoder.TryEncodeAsync(page.SourcePath, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Page encode failed for {Path}; keeping original.", page.SourcePath);
            encoded = null;
        }

        if (encoded is null)
        {
            return ResolvedPage.Original(originalExt);
        }

        var originalSize = new FileInfo(page.SourcePath).Length;
        return encoded.Bytes.Length < originalSize
            ? ResolvedPage.Encoded(encoded.Bytes, encoded.Extension)
            : ResolvedPage.Original(originalExt);
    }
}
