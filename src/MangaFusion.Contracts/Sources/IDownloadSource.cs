using MangaFusion.Contracts.Models;

namespace MangaFusion.Contracts.Sources;

/// <summary>A source that can resolve a chapter's downloadable page images. Defined in M1 and
/// consumed by the download orchestrator in M2.</summary>
public interface IDownloadSource : ISource
{
    Task<SourcePageSet> GetPagesAsync(
        string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default);
}
