using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Reads pages out of a folder artifact. Guards against path traversal by asserting the
/// resolved path stays under one of the configured library roots before touching the filesystem.
/// Deliberately "any root", not "the right root": this is an escape check (don't read /etc/passwd), and
/// resolving against the correct library is the caller's job.</summary>
public sealed class FolderArtifactReader(LibraryPaths paths) : IArtifactReader
{
    public StorageFormat Format => StorageFormat.Folder;

    public Task<IReadOnlyList<PageEntry>> ListPagesAsync(string absolutePath, CancellationToken ct = default)
    {
        EnsureUnderRoot(absolutePath);
        var dir = new DirectoryInfo(absolutePath);
        IReadOnlyList<PageEntry> pages = dir.Exists
            ? PageFiles(dir).Select((f, i) => new PageEntry(i, f.Name, ImageContentType.ForName(f.Name), f.Length)).ToList()
            : [];
        return Task.FromResult(pages);
    }

    public Task<PageContent?> OpenPageAsync(string absolutePath, int index, CancellationToken ct = default)
    {
        EnsureUnderRoot(absolutePath);
        var dir = new DirectoryInfo(absolutePath);
        var file = index < 0 || !dir.Exists ? null : PageFiles(dir).ElementAtOrDefault(index);
        if (file is null)
        {
            return Task.FromResult<PageContent?>(null);
        }

        EnsureUnderRoot(file.FullName);
        Stream stream = new FileStream(
            file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
        return Task.FromResult<PageContent?>(new PageContent(stream, ImageContentType.ForName(file.Name), file.Length));
    }

    private static IEnumerable<FileInfo> PageFiles(DirectoryInfo dir) => dir
        .EnumerateFiles()
        .Where(f => ImageContentType.IsImage(f.Name))
        .OrderBy(f => f.Name, StringComparer.Ordinal);

    private void EnsureUnderRoot(string path)
    {
        if (!paths.IsUnderAnyRoot(path))
        {
            throw new UnauthorizedAccessException("Artifact path escapes the library roots.");
        }
    }
}
