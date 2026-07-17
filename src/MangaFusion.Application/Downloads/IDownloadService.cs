using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Downloads;

/// <summary>Queues chapter downloads (as Hangfire jobs) and surfaces recent download activity.</summary>
public interface IDownloadService
{
    /// <summary>Queues a download of a chapter. If <paramref name="releaseId"/> is null, the newest
    /// non-external release is used (group-preference selection arrives in phase 5).</summary>
    Task<Guid> QueueChapterDownloadAsync(Guid chapterId, Guid? releaseId = null, CancellationToken ct = default);

    /// <summary>Queues best-release downloads for every not-yet-downloaded chapter (optionally filtered
    /// by language). Returns how many were queued.</summary>
    Task<int> QueueSeriesMissingAsync(Guid seriesId, IReadOnlyList<string> languages, CancellationToken ct = default);

    Task<IReadOnlyList<Download>> GetRecentAsync(int limit = 50, CancellationToken ct = default);
}
