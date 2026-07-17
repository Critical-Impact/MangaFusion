using System.IO.Compression;
using System.Security.Cryptography;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Writes a single <c>.cbz</c> (zip) with zero-padded page images + a ComicInfo.xml. Pages are
/// first offered to <see cref="PageEncodingResolver"/> (lossless WebP re-encode where that shrinks
/// them; otherwise the source bytes are kept as-is). Whatever bytes result are stored uncompressed
/// (already compressed, either way) for speed. Writes to a temp file then moves into place so a
/// partial file is never left behind, onto a path claimed via <see cref="LibraryPaths.UniquePath"/> so a
/// write never lands on bytes another artifact's row still points at.</summary>
public sealed class CbzChapterWriter(PageEncodingResolver resolver) : IChapterWriter
{
    public StorageFormat Format => StorageFormat.Cbz;

    public async Task<WriteResult> WriteAsync(
        WriteRequest request, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(request.TargetDirectory);
        var path = LibraryPaths.UniquePath(Path.Combine(request.TargetDirectory, request.FileBaseName + ".cbz"));
        // Unique per attempt, not just per artifact: a shared "<path>.tmp" is a second way two writes of
        // the same base name collide, and it would be the one file both attempts hold open at once.
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        var total = request.Segments.Sum(s => s.Pages.Count);
        var pad = Math.Max(3, total.ToString().Length);

        try
        {
            await using (var file = File.Create(tempPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                var index = 1;
                var done = 0;
                foreach (var segment in request.Segments)
                {
                    foreach (var page in segment.Pages.OrderBy(p => p.Index))
                    {
                        ct.ThrowIfCancellationRequested();

                        var resolved = await resolver.ResolveAsync(page, ct);

                        var entry = zip.CreateEntry(
                            $"{index.ToString().PadLeft(pad, '0')}{resolved.Extension}", CompressionLevel.NoCompression);
                        await using var entryStream = entry.Open();
                        if (resolved.Bytes is not null)
                        {
                            await entryStream.WriteAsync(resolved.Bytes, ct);
                        }
                        else
                        {
                            await using var source = File.OpenRead(page.SourcePath);
                            await source.CopyToAsync(entryStream, ct);
                        }

                        index++;
                        progress?.Report(++done * 100 / Math.Max(1, total));
                    }
                }

                var comicInfo = zip.CreateEntry("ComicInfo.xml");
                await using var comicInfoStream = comicInfo.Open();
                await ComicInfoXml.WriteAsync(comicInfoStream, request, total, ct);
            }

            var (hash, size) = await HashAsync(tempPath, ct);

            // overwrite: false — UniquePath already claimed a free path, so anything there now is another
            // writer that raced us to it. Failing is right: silently overwriting is the bug this guards.
            File.Move(tempPath, path, overwrite: false);
            return new WriteResult(path, size, total, hash);
        }
        finally
        {
            // Cancelled or failed part-way: the temp file is uniquely named, so nothing else will ever
            // reclaim it and it would leak into the library directory.
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static async Task<(string Hash, long Size)> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return (Convert.ToHexStringLower(hash), stream.Length);
    }
}
