using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using MangaFusion.Application.Tasks;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Infrastructure.Monitoring;

namespace MangaFusion.Infrastructure.Tasks;

/// <summary>Reads Hangfire's monitoring API and classifies jobs to MangaFusion task kinds by matching
/// their method against the known job types. Takes <see cref="JobStorage"/> from DI (not the static
/// <c>JobStorage.Current</c>) so each host uses its own storage.</summary>
public sealed class HangfireTaskQuery(JobStorage storage) : IBackgroundTaskQuery
{
    public Task<BackgroundStats> GetStatsAsync(CancellationToken ct = default)
    {
        var s = storage.GetMonitoringApi().GetStatistics();
        return Task.FromResult(new BackgroundStats(
            (int)s.Enqueued, (int)s.Processing, (int)s.Succeeded, (int)s.Failed, (int)s.Scheduled, (int)s.Servers));
    }

    public Task<IReadOnlyList<BackgroundJobInfo>> GetJobsAsync(int limit, CancellationToken ct = default)
    {
        var api = storage.GetMonitoringApi();
        var jobs = new List<BackgroundJobInfo>();

        foreach (var (id, dto) in api.ProcessingJobs(0, limit))
        {
            Add(jobs, id, dto.Job, TaskState.Running, error: null, startedAt: dto.StartedAt, finishedAt: null);
        }

        foreach (var (id, dto) in api.EnqueuedJobs("default", 0, limit))
        {
            Add(jobs, id, dto.Job, TaskState.Queued, error: null, startedAt: null, finishedAt: null);
        }

        foreach (var (id, dto) in api.ScheduledJobs(0, limit))
        {
            Add(jobs, id, dto.Job, TaskState.Scheduled, error: null, startedAt: null, finishedAt: null);
        }

        foreach (var (id, dto) in api.FailedJobs(0, limit))
        {
            Add(jobs, id, dto.Job, TaskState.Failed, dto.ExceptionMessage ?? dto.Reason, startedAt: null, finishedAt: dto.FailedAt);
        }

        foreach (var (id, dto) in api.SucceededJobs(0, limit))
        {
            Add(jobs, id, dto.Job, TaskState.Succeeded, error: null, startedAt: null, finishedAt: dto.SucceededAt);
        }

        return Task.FromResult<IReadOnlyList<BackgroundJobInfo>>(jobs);
    }

    private static void Add(
        List<BackgroundJobInfo> jobs, string id, Job? job, TaskState state,
        string? error, DateTime? startedAt, DateTime? finishedAt)
    {
        var (kind, downloadId, seriesId) = Classify(job);
        if (kind == TaskKind.Unknown)
        {
            return; // ignore jobs that aren't ours
        }

        jobs.Add(new BackgroundJobInfo(
            id, kind, downloadId, seriesId, state, error, AsOffset(startedAt), AsOffset(finishedAt)));
    }

    private static (TaskKind Kind, Guid? DownloadId, Guid? SeriesId) Classify(Job? job)
    {
        var method = job?.Method;
        if (method is null)
        {
            return (TaskKind.Unknown, null, null);
        }

        var type = method.DeclaringType;
        if (type == typeof(DownloadOrchestrator) && method.Name == nameof(DownloadOrchestrator.RunAsync))
        {
            return (TaskKind.Download, ArgGuid(job!, 0), null);
        }

        if (type == typeof(MonitorService) && method.Name == nameof(MonitorService.ScanSeriesAsync))
        {
            return (TaskKind.SeriesScan, null, ArgGuid(job!, 0));
        }

        if (type == typeof(MonitorScanJob) && method.Name == nameof(MonitorScanJob.ScanAllAsync))
        {
            return (TaskKind.LibraryScan, null, null);
        }

        return (TaskKind.Unknown, null, null);
    }

    private static Guid? ArgGuid(Job job, int index)
    {
        if (job.Args.Count <= index)
        {
            return null;
        }

        return job.Args[index] switch
        {
            Guid g => g,
            string s when Guid.TryParse(s, out var parsed) => parsed,
            _ => null,
        };
    }

    private static DateTimeOffset? AsOffset(DateTime? value) =>
        value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
}
