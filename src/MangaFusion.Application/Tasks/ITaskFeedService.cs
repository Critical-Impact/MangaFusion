namespace MangaFusion.Application.Tasks;

/// <summary>One row in the admin tasks view — a download (from the Downloads table) or a scan
/// (from the queue engine), normalized to a common shape.</summary>
public sealed record TaskFeedItem(
    string Id,
    string Kind,
    string Target,
    Guid? SeriesId,
    string State,
    int? PagesDone,
    int? PagesTotal,
    string? Error,
    string? HangfireJobId,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

public sealed record TaskFeed(BackgroundStats Stats, IReadOnlyList<TaskFeedItem> Items);

/// <summary>Builds the admin tasks feed by merging the Downloads table (rich download detail) with the
/// queue engine's scan jobs, and drives task actions.</summary>
public interface ITaskFeedService
{
    Task<TaskFeed> GetFeedAsync(int limit, CancellationToken ct = default);

    /// <summary>Re-queues a failed download (a new attempt for its release). Throws
    /// <see cref="InvalidOperationException"/> if the download is missing or not in a failed state.</summary>
    Task<Guid> RetryDownloadAsync(Guid downloadId, CancellationToken ct = default);
}
