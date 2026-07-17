using System.IO.Compression;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Reading;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Re-encodes the page images of an already-on-disk CBZ/folder artifact in place, via the same
/// <see cref="PageEncodingResolver"/> policy the chapter writers use. Unlike them, there's no
/// <see cref="PageFile"/>/<see cref="IChapterWriter"/> pipeline here — this operates directly on a
/// pre-built artifact (used by the old-downloader migration path, which moves external files into the
/// library rather than constructing them itself).</summary>
public sealed class ArtifactPageReencoder(PageEncodingResolver resolver, LibraryPaths paths)
{
    public Task ReencodeAsync(string path, StorageFormat format, CancellationToken ct) =>
        format == StorageFormat.Cbz ? ReencodeCbzAsync(path, ct) : ReencodeFolderAsync(path, ct);

    private async Task ReencodeCbzAsync(string cbzPath, CancellationToken ct)
    {
        var tempDir = paths.NewTempDirectory("mf-migrate-reencode");
        try
        {
            using var zip = ZipFile.Open(cbzPath, ZipArchiveMode.Update);
            // Snapshot first — deleting/creating entries while enumerating zip.Entries isn't safe.
            var imageEntries = zip.Entries.Where(e => ImageContentType.IsImage(e.Name)).ToList();

            foreach (var entry in imageEntries)
            {
                ct.ThrowIfCancellationRequested();

                var tempPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}{Path.GetExtension(entry.Name)}");
                await using (var entryStream = entry.Open())
                await using (var tempFile = File.Create(tempPath))
                {
                    await entryStream.CopyToAsync(tempFile, ct);
                }

                var resolved = await resolver.ResolveAsync(new PageFile(0, entry.FullName, tempPath), ct);
                if (resolved.Bytes is null)
                {
                    continue; // not smaller / declined — leave this entry untouched
                }

                var newName = Path.ChangeExtension(entry.FullName, resolved.Extension);
                entry.Delete();
                var newEntry = zip.CreateEntry(newName, CompressionLevel.NoCompression);
                await using var newEntryStream = newEntry.Open();
                await newEntryStream.WriteAsync(resolved.Bytes, ct);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private async Task ReencodeFolderAsync(string dirPath, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(dirPath)
                     .Where(f => ImageContentType.IsImage(Path.GetFileName(f))).ToList())
        {
            ct.ThrowIfCancellationRequested();

            var resolved = await resolver.ResolveAsync(new PageFile(0, Path.GetFileName(file), file), ct);
            if (resolved.Bytes is null)
            {
                continue;
            }

            var newPath = Path.ChangeExtension(file, resolved.Extension);
            await File.WriteAllBytesAsync(newPath, resolved.Bytes, ct);
            if (!string.Equals(newPath, file, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }
}
