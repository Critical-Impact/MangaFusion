namespace MangaFusion.Application.Library;

public sealed record MigrationBatchSummary(
    Guid Id, DateTimeOffset CreatedAt, string Status, int SeriesCount, string? Error);

public sealed record MigrationItemDetail(
    Guid Id,
    string FileName,
    string? UuidPrefix,
    string? Number,
    string? ChapterTitle,
    int PageCount,
    long SizeBytes,
    string? MatchedGroup,
    string Disposition,
    bool IsWinner,
    string? FlagReason);

public sealed record MigrationSeriesDetail(
    Guid Id,
    string FolderName,
    string? ComicInfoSeriesTitle,
    string? MatchedSourceSeriesId,
    string? MatchedTitle,
    string Regime,
    double Confidence,
    string Status,
    string? ConflictReason,
    /// <summary>True when the series' <em>only</em> flagged condition is the partial-purge ranking
    /// one — the target set for <see cref="IMigrationService.ClearRankingOnlyConflictsAsync"/>.</summary>
    bool HasRankingOnlyConflict,
    Guid? ExistingLibrarySeriesId,
    Guid? CommittedLibrarySeriesId,
    IReadOnlyList<string> GroupRanking,
    IReadOnlyList<MigrationItemDetail> Items,
    int? CommitItemsDone,
    int? CommitItemsTotal,
    /// <summary>True when Status is "Committing" but the background job behind it is no longer
    /// actually running (e.g. the app restarted mid-commit) — nothing is coming back to finish this
    /// series, so <see cref="IMigrationService.ResetStuckSeriesCommitAsync"/> is offered instead of
    /// waiting on progress that will never arrive.</summary>
    bool CommitJobCrashed);

public sealed record MigrationBatchDetail(
    Guid Id, DateTimeOffset CreatedAt, string Status, string? Error,
    IReadOnlyList<string> DivertedFolders, IReadOnlyList<MigrationSeriesDetail> Series,
    int? CommitSeriesDone, int? CommitSeriesTotal,
    /// <summary>Same crash detection as <see cref="MigrationSeriesDetail.CommitJobCrashed"/>, for the
    /// bulk "commit all clean" job — <see cref="IMigrationService.CancelCommitAllCleanAsync"/> both
    /// cancels a still-running bulk commit and recovers a crashed one.</summary>
    bool CommitJobCrashed);

/// <summary>Migrates CBZ files from an old (non-MangaFusion) downloader's inbox layout — one
/// subfolder per series, one file per chapter — into the library. Scans + matches against MangaDex
/// in the background; every series is held for review afterwards (regardless of how clean the match
/// is), since committing also re-encodes pages and can take a while — see <see cref="StartCommitAllCleanAsync"/>
/// for clearing the no-conflict majority in one action. Nothing on disk moves until a series is
/// committed.</summary>
public interface IMigrationService
{
    /// <summary>Scans the configured inbox and enqueues the match pipeline. Returns the new batch id
    /// immediately — poll <see cref="GetBatchAsync"/> for progress.</summary>
    Task<Guid> StartScanAsync(CancellationToken ct = default);

    /// <summary>Runs the scan + match pipeline for a batch — matches every folder against MangaDex and
    /// leaves every resulting series in <c>NeedsReview</c>, never auto-committing. Hangfire job entry
    /// point — not normally called directly.</summary>
    Task RunScanAsync(Guid batchId, CancellationToken ct);

    Task<IReadOnlyList<MigrationBatchSummary>> ListBatchesAsync(CancellationToken ct = default);

    Task<MigrationBatchDetail?> GetBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Re-resolves a series against a specific MangaDex series id (or clears the match when
    /// null), re-running feed matching and dedup. Held for review afterwards regardless of outcome.</summary>
    Task RematchSeriesAsync(Guid migrationSeriesId, string? sourceSeriesId, CancellationToken ct = default);

    /// <summary>Overrides one item's disposition/winner flag during manual conflict resolution.
    /// Disposition is one of "Import"/"Duplicate"/"Quarantine". Setting a second item to "Import"
    /// within the same chapter number automatically demotes the previous winner.</summary>
    Task SetItemDispositionAsync(
        Guid migrationItemId, string disposition, CancellationToken ct = default);

    /// <summary>Points the series at an existing library series to merge into (its metadata is never
    /// overwritten) instead of creating a new one; pass null to clear.</summary>
    Task SetMergeTargetAsync(Guid migrationSeriesId, Guid? existingLibrarySeriesId, CancellationToken ct = default);

    /// <summary>Enqueues a background commit of a reviewed series (create/merge the library series, move
    /// its files, re-encode pages) and returns immediately — the batch sits in <c>Committing</c> while it
    /// runs, poll it for completion. Validates up front and throws if the series is missing or already
    /// committed. The heavy work runs off the request thread so a large series can't be killed by a
    /// client/proxy timeout mid-commit.</summary>
    Task StartCommitSeriesAsync(Guid migrationSeriesId, CancellationToken ct = default);

    /// <summary>Hangfire job entry point for <see cref="StartCommitSeriesAsync"/> — not normally called
    /// directly.</summary>
    Task RunCommitSeriesAsync(Guid migrationSeriesId, CancellationToken ct);

    /// <summary>Enqueues a background commit of every not-yet-committed series in the batch that has no
    /// conflict and a resolved (non-Unmatched) regime — the unambiguous majority a full scan typically
    /// produces. Returns immediately; the batch sits in <c>Committing</c> while it runs. A failure on
    /// one series is recorded on it and doesn't stop the rest.</summary>
    Task StartCommitAllCleanAsync(Guid batchId, CancellationToken ct = default);

    // No RunCommitAllCleanAsync declared here (unlike the other Run* job entry points above): making it
    // genuinely cancellable needs a Hangfire-specific IJobCancellationToken parameter, which this
    // Hangfire-agnostic Application interface shouldn't depend on. Hangfire enqueues it against the
    // concrete MigrationService class directly (see StartCommitAllCleanAsync's implementation), the same
    // way it always resolves the concrete type rather than the interface for job activation.

    /// <summary>Stops the batch's in-flight bulk commit: if its Hangfire job is still actually running,
    /// cooperatively cancels it between series (already-committed series keep their status; the rest
    /// stay NeedsReview — the job's own cancellation handling does this once it notices, same as a
    /// graceful shutdown mid-commit). If the job has already crashed — e.g. the app restarted mid-commit,
    /// so nothing ever cleared Committing — resets the batch's stuck state directly instead. Throws if
    /// the batch isn't currently committing.</summary>
    Task CancelCommitAllCleanAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Recovers a single series stuck at Committing because its commit job crashed (see
    /// <see cref="MigrationSeriesDetail.CommitJobCrashed"/>) — reverts it to NeedsReview so it can be
    /// retried. Throws if the series isn't currently committing, or if its job still looks alive (a
    /// single-series commit isn't cancellable while genuinely running — only a confirmed-dead one can be
    /// reset).</summary>
    Task ResetStuckSeriesCommitAsync(Guid migrationSeriesId, CancellationToken ct = default);

    /// <summary>Clears the conflict status on a series that is to be migrated.</summary>
    Task ClearConflictAsync(Guid migrationSeriesId, CancellationToken ct = default);

    /// <summary>Batch-clears the conflict on every not-yet-committed series in the batch whose only
    /// flagged condition is the partial-purge ranking one (see <see cref="MigrationSeriesDetail.HasRankingOnlyConflict"/>).
    /// Series with any other flagged condition (ambiguous items, etc.) are left untouched, since those
    /// still need manual review. Returns how many series were cleared.</summary>
    Task<int> ClearRankingOnlyConflictsAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Drops a not-yet-committed series from the batch without importing anything: its inbox
    /// folder is moved whole to the outbox (so it stops showing up on the next scan) and the
    /// <see cref="MigrationSeriesDetail"/> row is deleted.</summary>
    Task RemoveSeriesAsync(Guid migrationSeriesId, CancellationToken ct = default);
}
