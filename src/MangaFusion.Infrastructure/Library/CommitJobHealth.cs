using Hangfire;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Tells whether a commit's recorded Hangfire job id still looks alive, so a series/batch
/// stuck at "Committing" because the process crashed mid-job — nothing left to ever clear its status —
/// can be told apart from one that's still legitimately running.</summary>
public sealed class CommitJobHealth(JobStorage storage)
{
    private static readonly HashSet<string> AliveStates = new(StringComparer.Ordinal)
    {
        "Processing", "Enqueued", "Scheduled", "Awaiting",
    };

    /// <summary>True when <paramref name="jobId"/> is set but Hangfire no longer considers it alive
    /// (succeeded, failed, deleted, or its data has expired/is unknown) — i.e. nothing is ever coming
    /// back to clear the Committing status this job id was recorded against. False (not crashed) when
    /// no job id was recorded at all, so a mid-enqueue race never shows a false crash warning.</summary>
    public bool IsCrashed(string? jobId)
    {
        if (jobId is null)
        {
            return false;
        }

        using var connection = storage.GetConnection();
        var state = connection.GetJobData(jobId)?.State;
        return state is null || !AliveStates.Contains(state);
    }
}
