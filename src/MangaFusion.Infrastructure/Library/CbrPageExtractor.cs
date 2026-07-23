using MangaFusion.Infrastructure.Reading;
using SharpCompress.Archives.Rar;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Reads pages out of a CBR (RAR-archived comic) file so it can be imported through the same
/// page-file pipeline as a CBZ/folder/PDF source. RAR's compression format is proprietary, so
/// SharpCompress — like the rest of the RAR ecosystem — can only read RAR archives, never write them;
/// that's not a problem here since a CBR is only ever a page-image *source*, converted into the
/// library's canonical Cbz/Folder <see cref="MangaFusion.Domain.Library.StorageFormat"/> on import,
/// exactly like <see cref="PdfPageExtractor"/> handles PDF.</summary>
public sealed class CbrPageExtractor
{
    public int CountPages(string path)
    {
        using var archive = RarArchive.Open(path);
        return archive.Entries.Count(e => !e.IsDirectory && ImageContentType.IsImage(e.Key ?? string.Empty));
    }

    public async Task<List<string>> ExtractPagesAsync(string path, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);
        var results = new List<string>();

        using var archive = RarArchive.Open(path);
        var entries = archive.Entries
            .Where(e => !e.IsDirectory && ImageContentType.IsImage(e.Key ?? string.Empty))
            .OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase);

        var index = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(destDir, $"{(index + 1):D5}{Path.GetExtension(entry.Key)}");
            await using (var entryStream = entry.OpenEntryStream())
            await using (var fileStream = File.Create(dest))
            {
                await entryStream.CopyToAsync(fileStream, ct);
            }

            results.Add(dest);
            index++;
        }

        return results;
    }
}
