using System.Security.Cryptography;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Writes page images (zero-padded) plus a ComicInfo.xml into a folder artifact. Pages are
/// first offered to <see cref="PageEncodingResolver"/> (lossless WebP re-encode where that shrinks
/// them; otherwise the source bytes are kept as-is). The target folder is claimed via
/// <see cref="LibraryPaths.UniquePath"/> so a write never lands in a folder another artifact owns.</summary>
public sealed class FolderChapterWriter(PageEncodingResolver resolver) : IChapterWriter
{
    public StorageFormat Format => StorageFormat.Folder;

    public async Task<WriteResult> WriteAsync(
        WriteRequest request, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var dir = LibraryPaths.UniquePath(Path.Combine(request.TargetDirectory, request.FileBaseName));
        Directory.CreateDirectory(dir);

        var total = request.Segments.Sum(s => s.Pages.Count);
        var pad = Math.Max(3, total.ToString().Length);

        var index = 1;
        var done = 0;
        long size = 0;
        using var sha = SHA256.Create();

        foreach (var segment in request.Segments)
        {
            foreach (var page in segment.Pages.OrderBy(p => p.Index))
            {
                ct.ThrowIfCancellationRequested();

                var resolved = await resolver.ResolveAsync(page, ct);
                var dest = Path.Combine(dir, $"{index.ToString().PadLeft(pad, '0')}{resolved.Extension}");

                byte[] bytes;
                if (resolved.Bytes is not null)
                {
                    bytes = resolved.Bytes;
                    await File.WriteAllBytesAsync(dest, bytes, ct);
                }
                else
                {
                    File.Copy(page.SourcePath, dest, overwrite: true);
                    bytes = await File.ReadAllBytesAsync(dest, ct);
                }

                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                size += bytes.Length;

                index++;
                progress?.Report(++done * 100 / Math.Max(1, total));
            }
        }

        await using (var comicInfo = File.Create(Path.Combine(dir, "ComicInfo.xml")))
        {
            await ComicInfoXml.WriteAsync(comicInfo, request, total, ct);
        }

        sha.TransformFinalBlock([], 0, 0);
        var hash = Convert.ToHexStringLower(sha.Hash!);
        return new WriteResult(dir, size, total, hash);
    }
}
