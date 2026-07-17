namespace MangaFusion.Application.Tasks;

public enum TaskKind
{
    Unknown = 0,
    Download,
    SeriesScan,
    LibraryScan,
}

public enum TaskState
{
    Unknown = 0,
    Queued,
    Running,
    Succeeded,
    Failed,
    Scheduled,
}

/// <summary>A background job as seen by the queue engine (Hangfire), classified to a MangaFusion task
/// kind. For download jobs the rich detail lives in the Downloads table; this carries the correlating
/// ids so the two can be joined.</summary>
public sealed record BackgroundJobInfo(
    string JobId,
    TaskKind Kind,
    Guid? DownloadId,
    Guid? SeriesId,
    TaskState State,
    string? Error,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>Queue-wide counters for the header strip.</summary>
public sealed record BackgroundStats(int Enqueued, int Processing, int Succeeded, int Failed, int Scheduled, int Servers);

/// <summary>Reads the background-job engine's state. Abstracts Hangfire's monitoring API so the Web
/// layer stays Hangfire-free and the feed merge is testable with a fake.</summary>
public interface IBackgroundTaskQuery
{
    /// <summary>Recognized MangaFusion jobs across all engine states, most-recent first, up to limit.</summary>
    Task<IReadOnlyList<BackgroundJobInfo>> GetJobsAsync(int limit, CancellationToken ct = default);

    Task<BackgroundStats> GetStatsAsync(CancellationToken ct = default);
}
