using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Reading;

/// <summary>One image entry inside an artifact, in reading order (0-based, spanning all chapters the
/// artifact holds).</summary>
public sealed record PageEntry(int Index, string Name, string ContentType, long Length);

/// <summary>A single page's bytes ready to stream, plus its content type. Caller disposes the stream.</summary>
public sealed record PageContent(Stream Stream, string ContentType, long Length);

/// <summary>Reads pages out of a stored artifact, independent of its on-disk <see cref="StorageFormat"/>
/// (CBZ zip or image folder). Mirrors how <c>IChapterWriter</c> is keyed by format.</summary>
public interface IArtifactReader
{
    StorageFormat Format { get; }

    /// <summary>Ordered image entries across the whole artifact (excludes ComicInfo.xml / non-images).</summary>
    Task<IReadOnlyList<PageEntry>> ListPagesAsync(string absolutePath, CancellationToken ct = default);

    /// <summary>Opens one page's bytes by artifact-global index, or null if the index is out of range.</summary>
    Task<PageContent?> OpenPageAsync(string absolutePath, int index, CancellationToken ct = default);
}
