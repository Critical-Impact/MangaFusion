using System.IO.Compression;
using System.Security.Cryptography;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Reading;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Page-counting and hashing for a CBZ file or an image folder, shared by every path that
/// turns a raw file on disk into an <see cref="Artifact"/> (local import, migration).</summary>
public sealed class ArtifactFileInspector
{
    public int CountPages(string path, StorageFormat format) =>
        format == StorageFormat.Cbz ? CountCbzPages(path) : CountFolderPages(path);

    public int CountCbzPages(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Count(e => !string.IsNullOrEmpty(e.Name) && ImageContentType.IsImage(e.Name));
    }

    public int CountFolderPages(string path) =>
        Directory.EnumerateFiles(path).Count(f => ImageContentType.IsImage(Path.GetFileName(f)));

    public long DirectorySize(string path) =>
        Directory.EnumerateFiles(path).Sum(f => new FileInfo(f).Length);

    public async Task<string> HashAsync(string path, StorageFormat format, CancellationToken ct)
    {
        using var sha = SHA256.Create();
        if (format == StorageFormat.Cbz)
        {
            await using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(await sha.ComputeHashAsync(stream, ct));
        }

        foreach (var file in Directory.EnumerateFiles(path).OrderBy(f => f, StringComparer.Ordinal))
        {
            var bytes = await File.ReadAllBytesAsync(file, ct);
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexStringLower(sha.Hash!);
    }
}
