using Hangfire;
using Hangfire.Server;
using MangaFusion.Application.Library;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

public sealed class ImportService(
    AppDbContext db,
    ImportPaths paths,
    ImportMatcher matcher,
    ImportCommitter committer,
    ImportScanner scanner,
    IBackgroundJobClient jobs,
    ILibraryNotifier notifier,
    CommitJobHealth jobHealth,
    ILogger<ImportService> logger) : IImportService
{
    public async Task<Guid> StartScanAsync(MediaKind kind, CancellationToken ct = default)
    {
        var batch = new ImportBatch { Kind = kind };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        jobs.Enqueue<ImportService>(s => s.RunScanAsync(batch.Id, CancellationToken.None));
        return batch.Id;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunScanAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            return;
        }

        try
        {
            var groups = scanner.ScanInbox(paths.InboxRoot(batch.Kind), batch.Kind);
            logger.LogDebug(
                "Import scan {BatchId}: found {Count} series group(s) in the {Kind} inbox.",
                batchId, groups.Count, batch.Kind);

            foreach (var group in groups)
            {
                // Batch is set explicitly, not left to EF's navigation fixup: the merge-target guard reads
                // Batch.Kind while this entity is still only Added, and a null nav there would NRE.
                var importSeries = new ImportSeries
                {
                    BatchId = batch.Id, Batch = batch, GroupTitle = group.GroupTitle,
                };
                db.ImportSeries.Add(importSeries);

                foreach (var file in group.Files)
                {
                    // Manga and comics are distributed differently, so the same filename means different
                    // things. A manga release is one file per *volume* (a purchased digital volume), which
                    // is a whole-volume import with no chapter number — see ChapterNumber.Normalize. A comic
                    // release is one file per *issue* ("100 Bullets #017.cbz"), and the issue number is the
                    // chapter number. Either way the user can still correct both fields before committing.
                    var isComic = batch.Kind == MediaKind.Comic;

                    importSeries.Items.Add(new ImportItem
                    {
                        FolderName = file.FolderName,
                        FileName = file.FileName,
                        Format = ToDomainFormat(file.Kind),
                        ParsedVolume = file.ParsedVolume,
                        PageCount = file.PageCount,
                        SizeBytes = file.SizeBytes,
                        Number = isComic ? file.ParsedNumber : null,
                        Volume = file.ParsedVolume,
                    });
                }

                try
                {
                    // The file count is the strongest non-title signal available: a candidate volume with
                    // fewer issues than this folder has files can't be the right series.
                    var candidates = await matcher.SearchCandidatesAsync(
                        batch.Kind, group.GroupTitle, importSeries.Items.Count, ct);
                    var best = candidates.FirstOrDefault();
                    if (best is not null)
                    {
                        importSeries.MatchedSourceSeriesId = best.SourceSeriesId;
                        importSeries.MatchedTitle = best.Title;
                    }
                }
                catch (Exception ex)
                {
                    // A failed lookup shouldn't fail the whole scan — the series just starts with no
                    // suggested match; the user can search manually during review.
                    logger.LogWarning(
                        ex, "Import scan: {Source} search failed for {Title}.",
                        ImportMatcher.SourceFor(batch.Kind), group.GroupTitle);
                }

                await DetectMergeTargetAsync(importSeries, ct);
                importSeries.Status = ImportSeriesStatus.NeedsReview;
                await db.SaveChangesAsync(ct);
            }

            batch.Status = ImportBatchStatus.Done;
            logger.LogDebug("Import scan {BatchId}: done.", batchId);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import scan failed for batch {BatchId}", batchId);

            // Whatever the failing group had half-built is still sitting in the change tracker; saving now
            // would persist that partial group alongside the Failed status. Drop it, then re-read the batch
            // (Clear() detaches it too) to record the failure on its own.
            db.ChangeTracker.Clear();
            var failed = await db.ImportBatches.FirstOrDefaultAsync(b => b.Id == batchId, CancellationToken.None);
            if (failed is null)
            {
                return;
            }

            failed.Status = ImportBatchStatus.Failed;
            failed.Error = ex.Message;

            // Not `ct`: the likeliest reason we're here is that `ct` was cancelled (shutdown). Saving on it
            // would throw again and leave the batch stuck in Scanning forever, with the UI polling a status
            // that never resolves.
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<ImportBatchSummary>> ListBatchesAsync(
        MediaKind kind, CancellationToken ct = default) =>
        // Scoped to one library: batches from the manga inbox must not surface while the user is in the
        // light-novel (or comic) library — each import section only lists its own kind's scans.
        (await db.ImportBatches
            .Where(b => b.Kind == kind)
            .Include(b => b.Series)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct))
        .Select(b => new ImportBatchSummary(
            b.Id, b.CreatedAt, b.Status.ToString(), b.Series.Count, b.Error, b.Kind.ToString()))
        .ToList();

    public async Task<ImportBatchDetail?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.ImportBatches
            .Include(b => b.Series).ThenInclude(s => s.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);

        return batch is null ? null : ToDetail(batch);
    }

    public async Task<IReadOnlyList<ImportCandidate>> SearchCandidatesAsync(
        Guid importSeriesId, string query, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);

        var text = string.IsNullOrWhiteSpace(query) ? importSeries.GroupTitle : query.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // Ranked against the number of files this series is importing — same signal the scan used, so a
        // manual re-search can't quietly fall back to the source's raw ordering.
        var candidates = await matcher.SearchCandidatesAsync(
            importSeries.Batch.Kind, text, importSeries.Items.Count, ct);

        return candidates
            .Select(c => new ImportCandidate(
                c.SourceSeriesId, c.Title, c.AltTitles, c.CoverUrl, c.Year, c.ChapterCount, c.SiteUrl))
            .ToList();
    }

    public async Task SetSeriesMatchAsync(Guid importSeriesId, string? sourceSeriesId, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);
        EnsureEditable(importSeries);

        if (sourceSeriesId is null)
        {
            importSeries.MatchedSourceSeriesId = null;
            importSeries.MatchedTitle = null;
        }
        else
        {
            var matchSource = ImportMatcher.SourceFor(importSeries.Batch.Kind);
            var sourceSeries = await matcher.GetSeriesAsync(importSeries.Batch.Kind, sourceSeriesId, ct)
                ?? throw new InvalidOperationException($"{matchSource} series '{sourceSeriesId}' not found.");
            importSeries.MatchedSourceSeriesId = sourceSeriesId;
            importSeries.MatchedTitle = sourceSeries.Title;
        }

        // A title override is contextual to the specific matched series' alt-titles — clear it
        // whenever the match itself changes, so a stale pick from a different match can't linger.
        importSeries.TitleOverride = null;

        await db.SaveChangesAsync(ct);
    }

    public async Task SetTitleOverrideAsync(Guid importSeriesId, string? titleOverride, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);
        EnsureEditable(importSeries);

        importSeries.TitleOverride = string.IsNullOrWhiteSpace(titleOverride) ? null : titleOverride.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task SetMergeTargetAsync(
        Guid importSeriesId, Guid? existingLibrarySeriesId, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);
        EnsureEditable(importSeries);
        if (existingLibrarySeriesId is { } id)
        {
            await MergeTarget.EnsureInLibraryAsync(db, id, importSeries.Batch.Kind, ct);
        }

        importSeries.ExistingLibrarySeriesId = existingLibrarySeriesId;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetItemAsync(
        Guid importItemId, bool include, string? number, string? volume, string? title,
        CancellationToken ct = default)
    {
        var item = await db.ImportItems.Include(i => i.Series)
            .FirstOrDefaultAsync(i => i.Id == importItemId, ct)
            ?? throw new InvalidOperationException("Import item not found.");
        EnsureEditable(item.Series);

        // Its chapter is already in the library and its source file is gone — there is nothing left for
        // an edit here to affect, and letting the number change would just misdescribe what was imported.
        if (item.ImportedAt is not null)
        {
            throw new InvalidOperationException("This item has already been imported and can't be edited.");
        }

        item.Include = include;
        item.Number = number;
        item.Volume = volume;
        item.Title = title;
        await db.SaveChangesAsync(ct);
    }

    public async Task StartCommitAsync(Guid importSeriesId, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);
        EnsureEditable(importSeries);
        if (importSeries.Items.Count(i => i.Include) == 0)
        {
            throw new InvalidOperationException("No items are included for import.");
        }

        importSeries.Status = ImportSeriesStatus.Committing;
        importSeries.CommitError = null;
        await db.SaveChangesAsync(ct);

        var jobId = jobs.Enqueue<ImportService>(s => s.RunCommitAsync(importSeriesId, CancellationToken.None));
        importSeries.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunCommitAsync(Guid importSeriesId, CancellationToken ct)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);
        try
        {
            await committer.CommitAsync(importSeries, ct);
            // CommitAsync already saved Status = Committed; this is a separate save so a successful
            // commit doesn't leave a stale (but harmless) job id sitting on an otherwise-done series.
            importSeries.HangfireJobId = null;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Import commit failed for series {ImportSeriesId}", importSeriesId);

            // Revert to NeedsReview (not a dead-end Failed state) so the user can fix whatever's
            // wrong — a number collision, a since-moved source file — and just retry immediately.
            importSeries.Status = ImportSeriesStatus.NeedsReview;
            importSeries.CommitError = ex.Message;
            importSeries.CommitItemsDone = null;
            importSeries.CommitItemsTotal = null;
            importSeries.CommitPageDone = null;
            importSeries.CommitPageTotal = null;
            importSeries.HangfireJobId = null;
            await db.SaveChangesAsync(CancellationToken.None);

            try
            {
                await notifier.ImportCommitProgressAsync(importSeriesId, "Failed", 0, 0, null, null, CancellationToken.None);
            }
            catch
            {
                // best-effort — the polled batch status is the durable source of truth
            }

            // Rethrow so Hangfire's own dashboard records this job as Failed rather than Succeeded —
            // the app's NeedsReview/CommitError state above (not Hangfire's status) is what actually
            // drives the review UI, and AutomaticRetry(Attempts = 0) below ensures this doesn't trigger
            // an actual retry attempt.
            throw;
        }
    }

    public async Task StartCommitAllCleanAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.ImportBatches
            .Include(b => b.Series).ThenInclude(s => s.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new InvalidOperationException("Import batch not found.");

        // A single or bulk commit is already in flight somewhere in the batch. Starting another would let
        // the bulk job pick up a series being committed by a different job and commit it twice, so refuse
        // — the same guard the review UI enforces by disabling the button, made authoritative server-side.
        if (batch.Series.Any(s => s.Status == ImportSeriesStatus.Committing))
        {
            throw new InvalidOperationException("A commit is already in progress for this batch.");
        }

        var clean = batch.Series.Where(s => IsCommittableWithoutReview(s, batch.Kind)).ToList();
        if (clean.Count == 0)
        {
            throw new InvalidOperationException("No series are ready to commit without conflicts.");
        }

        // Flip every eligible series to Committing up front — not just the one the job is about to work
        // on — so the review UI reflects the queued state immediately and keeps polling (its "any series
        // is Committing" activity check is what keeps the poll alive). Unlike the migration tool there's
        // no batch-level Committing status to lean on, so this is what makes the bulk job visible.
        foreach (var series in clean)
        {
            series.Status = ImportSeriesStatus.Committing;
            series.CommitError = null;
        }

        await db.SaveChangesAsync(ct);

        // Hand the job the exact ids it should commit rather than letting it re-query by status: a series
        // being committed by a *different* job is also Committing, and a status-only query would scoop it
        // up and double-commit it. The job stamps its own Hangfire id onto each series when it runs (see
        // RunCommitAllCleanAsync) — the single-writer alternative to writing the id back here, which could
        // race the job's own completion and leave a Committed series pointing at a finished job.
        var ids = clean.Select(s => s.Id).ToList();
        jobs.Enqueue<ImportService>(s => s.RunCommitAllCleanAsync(batchId, ids, null!, CancellationToken.None));
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunCommitAllCleanAsync(
        Guid batchId, List<Guid> seriesIds, PerformContext context, CancellationToken ct)
    {
        // Exactly the series StartCommitAllCleanAsync flipped for this job — no other. Re-read fresh (the
        // job can run much later) and drop any that are no longer Committing (e.g. reset in the meantime),
        // then commit each; a failure on one is recorded on it and doesn't stop the rest.
        var candidates = await db.ImportSeries
            .Include(s => s.Items)
            .Include(s => s.Batch)
            .Where(s => s.BatchId == batchId && seriesIds.Contains(s.Id)
                && s.Status == ImportSeriesStatus.Committing)
            .ToListAsync(ct);

        // Stamp this job's id onto its series as a single writer, so per-series crash detection
        // (CommitJobCrashed) and ResetStuckCommit recovery can tell whether the owning job is alive.
        var jobId = context?.BackgroundJob?.Id;
        if (jobId is not null)
        {
            foreach (var series in candidates)
            {
                series.HangfireJobId = jobId;
            }

            await db.SaveChangesAsync(ct);
        }

        foreach (var importSeries in candidates)
        {
            try
            {
                await committer.CommitAsync(importSeries, ct);
                // CommitAsync already saved Status = Committed; clear the (now stale) bulk job id off it.
                importSeries.HangfireJobId = null;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import bulk commit failed for series {ImportSeriesId} ({Title}).",
                    importSeries.Id, importSeries.GroupTitle);

                // Revert to NeedsReview (not a dead-end Failed state) so the user can fix whatever's
                // wrong and retry — a number collision against an existing chapter, a since-moved source
                // file. One series failing must not abort the others still queued behind it.
                importSeries.Status = ImportSeriesStatus.NeedsReview;
                importSeries.CommitError = ex.Message;
                importSeries.CommitItemsDone = null;
                importSeries.CommitItemsTotal = null;
                importSeries.CommitPageDone = null;
                importSeries.CommitPageTotal = null;
                importSeries.HangfireJobId = null;
                await db.SaveChangesAsync(CancellationToken.None);

                try
                {
                    await notifier.ImportCommitProgressAsync(
                        importSeries.Id, "Failed", 0, 0, null, null, CancellationToken.None);
                }
                catch
                {
                    // best-effort — the polled batch status is the durable source of truth
                }
            }
        }
    }

    /// <summary>Whether a bulk "commit all" should include this series: it's awaiting review, has at
    /// least one included item, and no two included items resolve to the same chapter number (which
    /// would collide on commit). Mirrors the per-series Commit button's own enable conditions — the UI's
    /// definition of a series that's ready with no conflicts. The against-existing-chapters collision the
    /// committer also guards isn't pre-checked here: if it does happen the series' own commit fails and
    /// reverts to NeedsReview without stopping the rest, exactly as a per-series commit would.</summary>
    private static bool IsCommittableWithoutReview(ImportSeries series, MediaKind kind)
    {
        if (series.Status != ImportSeriesStatus.NeedsReview)
        {
            return false;
        }

        var included = series.Items.Where(i => i.Include).ToList();
        if (included.Count == 0)
        {
            return false;
        }

        // An included item the UI treats as a numbered chapter must actually carry a number — otherwise it
        // commits as a blank "oneshot" key the user never confirmed. Comic issues are always chapter-mode;
        // for manga a blank number legitimately means "whole volume", so only comics gate on it. Prose is
        // whole-volume by nature and exempt. Mirrors the frontend's hasIncompleteChapters check.
        if (kind == MediaKind.Comic
            && included.Any(i => !IsProse(i.Format) && string.IsNullOrWhiteSpace(i.Number)))
        {
            return false;
        }

        var seenKeys = new HashSet<string>();
        return included.All(i => seenKeys.Add(ChapterNumber.Normalize(i.Number, i.Volume).Key));
    }

    private static bool IsProse(ImportSourceFormat format) => format is
        ImportSourceFormat.ProseEpub or ImportSourceFormat.ProsePdf
        or ImportSourceFormat.ProseText or ImportSourceFormat.ProseMarkdown;

    /// <summary>Recovers a series stuck at Committing because its Hangfire commit job crashed (e.g. the
    /// app restarted mid-commit, so nothing ever ran RunCommitAsync's catch block) — same recovery as a
    /// normal commit failure. Throws if there's no commit in flight or its job still looks alive.</summary>
    public async Task ResetStuckCommitAsync(Guid importSeriesId, CancellationToken ct = default)
    {
        var importSeries = await LoadSeriesAsync(importSeriesId, ct);

        if (importSeries.Status != ImportSeriesStatus.Committing)
        {
            throw new InvalidOperationException("This series isn't currently committing.");
        }

        if (importSeries.HangfireJobId is not null && !jobHealth.IsCrashed(importSeries.HangfireJobId))
        {
            throw new InvalidOperationException(
                "This series' commit job still looks alive — it isn't cancellable while running.");
        }

        importSeries.Status = ImportSeriesStatus.NeedsReview;
        importSeries.CommitError = "Commit job crashed (the app likely restarted mid-commit) — check for partial writes before retrying.";
        importSeries.CommitItemsDone = null;
        importSeries.CommitItemsTotal = null;
        importSeries.CommitPageDone = null;
        importSeries.CommitPageTotal = null;
        importSeries.HangfireJobId = null;
        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Import commit for series {ImportSeriesId} had already crashed; reset to NeedsReview.", importSeriesId);
    }

    // --- internals -------------------------------------------------------------------------------

    /// <summary>Auto-suggests merging into an existing library series with the same title that isn't
    /// already linked to this MangaUpdates id — e.g. a hand-created local series. Never overwrites its
    /// metadata; only adds chapters/files on commit. The user can clear this in review.</summary>
    private async Task DetectMergeTargetAsync(ImportSeries importSeries, CancellationToken ct)
    {
        if (importSeries.MatchedSourceSeriesId is null)
        {
            importSeries.ExistingLibrarySeriesId = null;
            return;
        }

        var matchSourceId = ImportMatcher.SourceFor(importSeries.Batch.Kind);
        // Scoped to the batch's kind: a source entry may back a series per kind, so a manga series linked
        // to this MangaUpdates id must not make a light-novel import believe it's already linked.
        var alreadyLinked = await db.Series.AnyAsync(
            s => s.Kind == importSeries.Batch.Kind
                 && s.SourceLinks.Any(l => l.SourceId == matchSourceId
                                           && l.SourceSeriesId == importSeries.MatchedSourceSeriesId), ct);
        if (alreadyLinked)
        {
            importSeries.ExistingLibrarySeriesId = null; // the committer will find it by link
            return;
        }

        var candidates = new[] { importSeries.MatchedTitle, importSeries.GroupTitle }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.ToLowerInvariant())
            .Distinct()
            .ToList();

        var titleMatch = await MergeTarget.FindByTitleAsync(db, importSeries.Batch.Kind, candidates, ct);

        importSeries.ExistingLibrarySeriesId = titleMatch?.Id;
    }

    // Batch is included because the committer reads its MediaKind to decide which library the series
    // lands in.
    private async Task<ImportSeries> LoadSeriesAsync(Guid importSeriesId, CancellationToken ct) =>
        await db.ImportSeries.Include(s => s.Items).Include(s => s.Batch)
            .FirstOrDefaultAsync(s => s.Id == importSeriesId, ct)
        ?? throw new InvalidOperationException("Import series not found.");

    private static void EnsureEditable(ImportSeries series)
    {
        if (series.Status == ImportSeriesStatus.Committed)
        {
            throw new InvalidOperationException("This series has already been committed.");
        }

        if (series.Status == ImportSeriesStatus.Committing)
        {
            throw new InvalidOperationException("This series is already being committed.");
        }
    }

    private static ImportSourceFormat ToDomainFormat(ChapterSourceKind kind) => kind switch
    {
        ChapterSourceKind.Cbz => ImportSourceFormat.Cbz,
        ChapterSourceKind.Folder => ImportSourceFormat.Folder,
        ChapterSourceKind.Pdf => ImportSourceFormat.Pdf,
        ChapterSourceKind.Cbr => ImportSourceFormat.Cbr,
        ChapterSourceKind.Epub => ImportSourceFormat.Epub,
        ChapterSourceKind.ProseEpub => ImportSourceFormat.ProseEpub,
        ChapterSourceKind.ProsePdf => ImportSourceFormat.ProsePdf,
        ChapterSourceKind.ProseText => ImportSourceFormat.ProseText,
        ChapterSourceKind.ProseMarkdown => ImportSourceFormat.ProseMarkdown,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private ImportBatchDetail ToDetail(ImportBatch batch) => new(
        batch.Id, batch.CreatedAt, batch.Status.ToString(), batch.Error,
        batch.Series.Select(ToDetail).ToList(),
        batch.Kind.ToString(),
        ImportMatcher.SourceFor(batch.Kind));

    private ImportSeriesDetail ToDetail(ImportSeries s) => new(
        s.Id, s.GroupTitle, s.MatchedSourceSeriesId, s.MatchedTitle, s.TitleOverride, s.Status.ToString(),
        s.ExistingLibrarySeriesId, s.CommittedLibrarySeriesId,
        s.CommitItemsDone, s.CommitItemsTotal, s.CommitPageDone, s.CommitPageTotal, s.CommitError,
        s.Items.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).Select(ToDetail).ToList(),
        s.Status == ImportSeriesStatus.Committing && jobHealth.IsCrashed(s.HangfireJobId));

    private static ImportItemDetail ToDetail(ImportItem i) => new(
        i.Id, i.FolderName, i.FileName, i.Format.ToString(), i.ParsedVolume, i.PageCount, i.SizeBytes,
        i.Include, i.Number, i.Volume, i.Title, i.ImportedAt is not null);
}
