namespace MangaFusion.Application.Library;

public sealed record ImportBatchSummary(
    Guid Id, DateTimeOffset CreatedAt, string Status, int SeriesCount, string? Error, string Kind);

public sealed record ImportItemDetail(
    Guid Id,
    string FolderName,
    string FileName,
    string Format,
    string? ParsedVolume,
    int PageCount,
    long SizeBytes,
    bool Include,
    string? Number,
    string? Volume,
    string? Title,
    /// <summary>Already written into the library by an earlier commit attempt — a retry skips it, and it
    /// can no longer be edited.</summary>
    bool Imported);

public sealed record ImportSeriesDetail(
    Guid Id,
    string GroupTitle,
    string? MatchedSourceSeriesId,
    string? MatchedTitle,
    string? TitleOverride,
    string Status,
    Guid? ExistingLibrarySeriesId,
    Guid? CommittedLibrarySeriesId,
    /// <summary>Progress while Status == "Committing" — a durable (DB-persisted) fallback for the live
    /// SignalR "importCommitProgress" push. CommitPageDone/Total are only set while the current item is
    /// a PDF (the slow case).</summary>
    int? CommitItemsDone,
    int? CommitItemsTotal,
    int? CommitPageDone,
    int? CommitPageTotal,
    string? CommitError,
    IReadOnlyList<ImportItemDetail> Items,
    /// <summary>True when Status is "Committing" but the background job behind it is no longer actually
    /// running (e.g. the app restarted mid-commit) — nothing is coming back to finish this series, so
    /// <see cref="IImportService.ResetStuckCommitAsync"/> is offered instead of waiting on progress that
    /// will never arrive.</summary>
    bool CommitJobCrashed);

/// <summary>One ranked match candidate, shaped for the review UI.
///
/// <see cref="Year"/> and <see cref="ChapterCount"/> are the fields that actually disambiguate: comics are
/// relaunched constantly under the same title, so a ComicVine search for "Batman" comes back with a dozen
/// volumes whose names are identical and whose start year and issue count are not. <see cref="SiteUrl"/>
/// lets the user open the candidate on the source's own site when the metadata still isn't enough.</summary>
public sealed record ImportCandidate(
    string SourceSeriesId,
    string Title,
    IReadOnlyList<string> AltTitles,
    string? CoverUrl,
    int? Year,
    int? ChapterCount,
    string? SiteUrl);

public sealed record ImportBatchDetail(
    Guid Id, DateTimeOffset CreatedAt, string Status, string? Error, IReadOnlyList<ImportSeriesDetail> Series,
    /// <summary>"Manga" | "Comic" — a string like the sibling Status, so the SPA never has to know the
    /// enum's numeric values.</summary>
    string Kind,
    /// <summary>The metadata source this batch's series are matched against — the UI searches it directly
    /// when the user corrects a match.</summary>
    string MatchSourceId);

/// <summary>Sonarr/Radarr-style import wizard for manually-sourced manga: scans an inbox of release
/// folders, groups same-title folders into series candidates, suggests a MangaUpdates match for each
/// (metadata only — MangaUpdates has no chapter API), and always waits for the user to confirm (or
/// correct/clear) the match before committing — title-only matching is not reliable enough to trust
/// unattended (see <c>ImportMatcher</c>/<c>MigrationMatcher</c>'s title-matching notes).</summary>
public interface IImportService
{
    /// <summary>Scans the configured import inbox and enqueues the match pipeline. Returns the new
    /// batch id immediately — poll <see cref="GetBatchAsync"/> for progress. <paramref name="kind"/>
    /// decides which library the batch commits into, and therefore which metadata source its series are
    /// matched against (see <c>ImportMatcher.SourceFor</c>).</summary>
    Task<Guid> StartScanAsync(MediaKind kind, CancellationToken ct = default);

    /// <summary>Runs the scan + match pipeline for a batch. Hangfire job entry point — not normally
    /// called directly.</summary>
    Task RunScanAsync(Guid batchId, CancellationToken ct);

    /// <summary>Import batches for one library only — scoped to <paramref name="kind"/> so a scan started
    /// in one mode doesn't surface in another.</summary>
    Task<IReadOnlyList<ImportBatchSummary>> ListBatchesAsync(MediaKind kind, CancellationToken ct = default);

    Task<ImportBatchDetail?> GetBatchAsync(Guid batchId, CancellationToken ct = default);

    /// <summary>Re-searches the batch's metadata source for this series, ranked the same way the scan ranks
    /// its initial suggestion — against the batch's source, and sanity-checked against how many files the
    /// series actually has. Going through the generic <c>/api/sources/{id}/search</c> instead would return
    /// the source's own raw ordering, which for comics puts eleven identically-titled volumes in arbitrary
    /// order.</summary>
    Task<IReadOnlyList<ImportCandidate>> SearchCandidatesAsync(
        Guid importSeriesId, string query, CancellationToken ct = default);

    /// <summary>Sets (or clears, when null) the series' match on the batch's metadata source. Always leaves
    /// it at NeedsReview — this wizard never auto-commits.</summary>
    Task SetSeriesMatchAsync(Guid importSeriesId, string? sourceSeriesId, CancellationToken ct = default);

    /// <summary>Points the series at an existing library series to merge into (its metadata is never
    /// overwritten) instead of creating a new one; pass null to clear.</summary>
    Task SetMergeTargetAsync(Guid importSeriesId, Guid? existingLibrarySeriesId, CancellationToken ct = default);

    /// <summary>Overrides the committed series' title with one of the matched MangaUpdates series' alt-
    /// titles (or any other value); pass null/blank to use the matched/primary title as-is. Ignored on
    /// commit when merging into an existing series.</summary>
    Task SetTitleOverrideAsync(Guid importSeriesId, string? titleOverride, CancellationToken ct = default);

    /// <summary>Updates one item's include flag and/or chapter-spec fields before commit.</summary>
    Task SetItemAsync(
        Guid importItemId, bool include, string? number, string? volume, string? title,
        CancellationToken ct = default);

    /// <summary>Marks a reviewed series as committing and enqueues the commit job — creating/merging
    /// the library series and importing its included files as chapters (PDF conversion can take
    /// minutes; this returns immediately, poll <see cref="GetBatchAsync"/> or listen for the live
    /// "importCommitProgress" push for progress). Throws synchronously if no items are included, since
    /// that's known without doing any work.</summary>
    Task StartCommitAsync(Guid importSeriesId, CancellationToken ct = default);

    /// <summary>Runs the commit for a series. Hangfire job entry point — not normally called directly.
    /// On failure, reverts the series to NeedsReview with <c>CommitError</c> set, rather than leaving
    /// it stuck at Committing or in a dead-end Failed state — the user can fix the issue and retry.</summary>
    Task RunCommitAsync(Guid importSeriesId, CancellationToken ct);

    /// <summary>Enqueues a single background job that commits every not-yet-committed series in the
    /// batch with no conflict — at least one included item and no two included items resolving to the
    /// same chapter number — the same conditions that enable the per-series Commit button, so the
    /// reviewed no-conflict majority can be cleared in one action (the import equivalent of the
    /// migration tool's "commit all clean matches"). Each eligible series is flipped to Committing
    /// immediately (so the review UI reflects the queued state and keeps polling) and the job commits
    /// them one at a time; a failure on one is recorded on it (reverted to NeedsReview with its
    /// CommitError) and doesn't stop the rest. Throws if the batch is missing or nothing is eligible.</summary>
    Task StartCommitAllCleanAsync(Guid batchId, CancellationToken ct = default);

    // No RunCommitAllCleanAsync declared here (unlike RunCommitAsync above): it takes a Hangfire
    // PerformContext (to stamp its own job id as a single writer) which this Hangfire-agnostic Application
    // interface shouldn't depend on. Hangfire enqueues it against the concrete ImportService class
    // directly — see StartCommitAllCleanAsync's implementation.

    /// <summary>Recovers a series stuck at Committing because its commit job crashed (see
    /// <see cref="ImportSeriesDetail.CommitJobCrashed"/>) — reverts it to NeedsReview so it can be
    /// retried, same as a normal commit failure. Throws if the series isn't currently committing, or if
    /// its job still looks alive (not cancellable while genuinely running — only a confirmed-dead job can
    /// be reset).</summary>
    Task ResetStuckCommitAsync(Guid importSeriesId, CancellationToken ct = default);
}
