using System.IO.Compression;
using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Reads pages out of a <c>.cbz</c> (zip). A page request copies just that one entry into a
/// MemoryStream and closes the archive immediately — cheap (one image), and avoids the concurrent-read
/// and disposal-ordering hazards of handing back a live <see cref="ZipArchive"/> entry stream.</summary>
public sealed class CbzArtifactReader : IArtifactReader
{
    public StorageFormat Format => StorageFormat.Cbz;

    public Task<IReadOnlyList<PageEntry>> ListPagesAsync(string absolutePath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(absolutePath);
        IReadOnlyList<PageEntry> pages = PageEntries(zip)
            .Select((e, i) => new PageEntry(i, e.FullName, ImageContentType.ForName(e.Name), e.Length))
            .ToList();
        return Task.FromResult(pages);
    }

    public async Task<PageContent?> OpenPageAsync(string absolutePath, int index, CancellationToken ct = default)
    {
        if (index < 0)
        {
            return null;
        }

        using var zip = ZipFile.OpenRead(absolutePath);
        var entry = PageEntries(zip).ElementAtOrDefault(index);
        if (entry is null)
        {
            return null;
        }

        var capacity = entry.Length is > 0 and < int.MaxValue ? (int)entry.Length : 0;
        var buffer = new MemoryStream(capacity);
        await using (var source = entry.Open())
        {
            await source.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;
        return new PageContent(buffer, ImageContentType.ForName(entry.Name), buffer.Length);
    }

    private static IEnumerable<ZipArchiveEntry> PageEntries(ZipArchive zip) => zip.Entries
        .Where(e => !string.IsNullOrEmpty(e.Name) && ImageContentType.IsImage(e.Name))
        .OrderBy(e => e.FullName, StringComparer.Ordinal);
}
