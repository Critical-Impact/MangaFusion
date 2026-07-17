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
    Guid? ExistingLibrarySeriesId,
    Guid? CommittedLibrarySeriesId,
    IReadOnlyList<string> GroupRanking,
    IReadOnlyList<MigrationItemDetail> Items);

public sealed record MigrationBatchDetail(
    Guid Id, DateTimeOffset CreatedAt, string Status, string? Error,
    IReadOnlyList<string> DivertedFolders, IReadOnlyList<MigrationSeriesDetail> Series);

/// <summary>Migrates CBZ files from an old (non-MangaFusion) downloader's inbox layout — one
/// subfolder per series, one file per chapter — into the library. Scans + matches against MangaDex
/// in the background; every series is held for review afterwards (regardless of how clean the match
/// is), since committing also re-encodes pages and can take a while — see <see cref="CommitAllCleanAsync"/>
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

    /// <summary>Commits a reviewed series: creates/merges the library series and moves its files.
    /// Throws if items are still pending/unresolved.</summary>
    Task CommitSeriesAsync(Guid migrationSeriesId, CancellationToken ct = default);

    /// <summary>Commits every not-yet-committed series in the batch that has no conflict and a
    /// resolved (non-Unmatched) regime — the unambiguous majority a full scan typically produces —
    /// without needing to click Commit on each individually. A failure on one series doesn't stop the
    /// rest. Returns the number actually committed.</summary>
    Task<int> CommitAllCleanAsync(Guid batchId, CancellationToken ct = default);
}
