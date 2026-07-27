using Hangfire;
using MangaFusion.Application.Library;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

public sealed class MigrationService(
    AppDbContext db,
    MigrationPaths paths,
    ImportPaths importPaths,
    MigrationMatcher matcher,
    MigrationCommitter committer,
    MigrationScanner scanner,
    IBackgroundJobClient jobs,
    ILibraryNotifier notifier,
    CommitJobHealth jobHealth,
    ILogger<MigrationService> logger) : IMigrationService
{
    public async Task<Guid> StartScanAsync(CancellationToken ct = default)
    {
        // Manga by construction, stated rather than left to the enum's default: this tool matches files by
        // their MangaDex chapter-UUID filename prefix and dedups by scanlation group, so there is no comic
        // equivalent for it to scan.
        var batch = new MigrationBatch { Kind = MediaKind.Manga };
        db.MigrationBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        jobs.Enqueue<MigrationService>(s => s.RunScanAsync(batch.Id, CancellationToken.None));
        return batch.Id;
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunScanAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            return;
        }

        try
        {
            var scanResult = scanner.ScanInbox(paths.InboxRoot());
            logger.LogDebug(
                "Migration scan {BatchId}: found {Count} series folder(s) in the inbox, {DivertedCount} without " +
                "ComicInfo.xml (diverted to the import inbox).",
                batchId, scanResult.Folders.Count, scanResult.FoldersWithNoComicInfo.Count);

            foreach (var dir in scanResult.FoldersWithNoComicInfo)
            {
                batch.DivertedFolders.Add(DivertToImportInbox(dir));
            }

            foreach (var folder in scanResult.Folders)
            {
                // Batch is set explicitly, not left to EF's navigation fixup: the merge-target guard reads
                // Batch.Kind while this entity is still only Added, and a null nav there would NRE.
                var migrationSeries = new MigrationSeries
                {
                    BatchId = batch.Id, Batch = batch, FolderName = folder.FolderName,
                };
                db.MigrationSeries.Add(migrationSeries);

                try
                {
                    await ProcessFolderAsync(migrationSeries, folder, ct);
                }
                catch (Exception ex)
                {
                    migrationSeries.Status = MigrationSeriesStatus.Failed;
                    migrationSeries.ConflictReason = $"Scan failed: {ex.Message}";
                    logger.LogError(ex, "Migration scan failed for folder {Folder}", folder.FolderName);
                }

                await db.SaveChangesAsync(ct);
            }

            batch.Status = MigrationBatchStatus.Done;
            logger.LogDebug("Migration scan {BatchId}: done.", batchId);
        }
        catch (Exception ex)
        {
            batch.Status = MigrationBatchStatus.Failed;
            batch.Error = ex.Message;
            logger.LogError(ex, "Migration scan failed for batch {BatchId}", batchId);
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MigrationBatchSummary>> ListBatchesAsync(CancellationToken ct = default) =>
        (await db.MigrationBatches
            .Include(b => b.Series)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct))
        .Select(b => new MigrationBatchSummary(b.Id, b.CreatedAt, b.Status.ToString(), b.Series.Count, b.Error))
        .ToList();

    public async Task<MigrationBatchDetail?> GetBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.MigrationBatches
            .Include(b => b.Series).ThenInclude(s => s.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Id == batchId, ct);

        return batch is null ? null : ToDetail(batch);
    }

    public async Task RematchSeriesAsync(Guid migrationSeriesId, string? sourceSeriesId, CancellationToken ct = default)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        EnsureNotCommitted(migrationSeries); // its inbox folder is gone once committed — nothing to rescan
        var folder = RescanFolder(migrationSeries.FolderName);

        if (sourceSeriesId is null)
        {
            migrationSeries.MatchedSourceId = null;
            migrationSeries.MatchedSourceSeriesId = null;
            migrationSeries.MatchedTitle = null;
            migrationSeries.Regime = MigrationRegime.Unmatched;
            migrationSeries.Confidence = 0;
            migrationSeries.GroupRanking = [];
            migrationSeries.Items.Clear();
            foreach (var file in folder.Files)
            {
                var pendingItem = ToPendingItem(file);
                migrationSeries.Items.Add(pendingItem);
                db.MigrationItems.Add(pendingItem); // force Added state (entity carries a client-set Guid key)
            }

            migrationSeries.Status = MigrationSeriesStatus.NeedsReview;
            migrationSeries.ConflictReason = "Match cleared; pick a MangaDex series.";
            migrationSeries.ConflictKind = MigrationConflictKind.None;
        }
        else
        {
            var match = await matcher.MatchAgainstSeriesAsync(folder, sourceSeriesId, ct);
            await ApplyMatchAsync(migrationSeries, match, ct);
            migrationSeries.Status = MigrationSeriesStatus.NeedsReview; // manual actions always wait for explicit commit
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task SetItemDispositionAsync(
        Guid migrationItemId, string disposition, CancellationToken ct = default)
    {
        var item = await db.MigrationItems.Include(i => i.Series)
            .FirstOrDefaultAsync(i => i.Id == migrationItemId, ct)
            ?? throw new InvalidOperationException("Migration item not found.");
        EnsureNotCommitted(item.Series);

        if (!Enum.TryParse<MigrationItemDisposition>(disposition, ignoreCase: true, out var parsed)
            || parsed == MigrationItemDisposition.Pending)
        {
            throw new InvalidOperationException("Disposition must be Import, Duplicate, or Quarantine.");
        }

        logger.LogDebug(
            "Migration review: item {ItemId} ({FileName}) disposition -> {Disposition}.",
            item.Id, item.FileName, parsed);

        if (parsed == MigrationItemDisposition.Import)
        {
            // Enforce a single winner per chapter number — demote whichever item currently holds it.
            var siblings = await db.MigrationItems
                .Where(i => i.MigrationSeriesId == item.MigrationSeriesId
                            && i.NumberKey == item.NumberKey
                            && i.Id != item.Id
                            && i.Disposition == MigrationItemDisposition.Import)
                .ToListAsync(ct);
            foreach (var sibling in siblings)
            {
                sibling.Disposition = MigrationItemDisposition.Duplicate;
                sibling.IsWinner = false;
                sibling.FlagReason = "Superseded by a manually-selected copy of this chapter.";
            }
        }

        item.Disposition = parsed;
        item.IsWinner = parsed == MigrationItemDisposition.Import;
        item.FlagReason = parsed == MigrationItemDisposition.Import ? null : item.FlagReason;

        await db.SaveChangesAsync(ct);
    }

    public async Task SetMergeTargetAsync(
        Guid migrationSeriesId, Guid? existingLibrarySeriesId, CancellationToken ct = default)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        EnsureNotCommitted(migrationSeries);
        if (existingLibrarySeriesId is { } id)
        {
            await MergeTarget.EnsureInLibraryAsync(db, id, migrationSeries.Batch.Kind, ct);
        }

        migrationSeries.ExistingLibrarySeriesId = existingLibrarySeriesId;
        await db.SaveChangesAsync(ct);
        logger.LogDebug(
            "Migration review: series {SeriesId} merge target -> {Target}.",
            migrationSeriesId, existingLibrarySeriesId?.ToString() ?? "(none — create new)");
    }

    public async Task StartCommitSeriesAsync(Guid migrationSeriesId, CancellationToken ct = default)
    {
        // Validate synchronously so a bad/already-committed request fails the HTTP call fast; the actual
        // commit is heavy (file moves + page re-encode) so it runs off the request thread — otherwise a
        // client/proxy timeout aborts it mid-write (RequestAborted would cancel the CopyToAsync).
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        EnsureNotCommitted(migrationSeries);

        migrationSeries.Batch.Status = MigrationBatchStatus.Committing;
        await db.SaveChangesAsync(ct);

        var jobId = jobs.Enqueue<MigrationService>(s => s.RunCommitSeriesAsync(migrationSeriesId, CancellationToken.None));
        migrationSeries.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);
    }

    [AutomaticRetry(Attempts = 0)]
    public async Task RunCommitSeriesAsync(Guid migrationSeriesId, CancellationToken ct)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        var batch = migrationSeries.Batch;
        logger.LogDebug("Migration review: committing series {SeriesId} ({Folder}).",
            migrationSeriesId, migrationSeries.FolderName);
        try
        {
            await committer.CommitAsync(migrationSeries, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown mid-commit — leave the series NeedsReview to retry, don't mark it Failed.
            migrationSeries.HangfireJobId = null;
            batch.Status = MigrationBatchStatus.Done;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            migrationSeries.Status = MigrationSeriesStatus.Failed;
            migrationSeries.ConflictReason = $"Commit failed: {ex.Message}";
            migrationSeries.CommitItemsDone = null;
            migrationSeries.CommitItemsTotal = null;
            logger.LogError(ex, "Migration commit failed for series {SeriesId} ({Folder}).",
                migrationSeries.Id, migrationSeries.FolderName);
        }

        migrationSeries.HangfireJobId = null;
        batch.Status = MigrationBatchStatus.Done;
        await db.SaveChangesAsync(CancellationToken.None);
    }

    public async Task StartCommitAllCleanAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            throw new InvalidOperationException("Migration batch not found.");
        }

        batch.Status = MigrationBatchStatus.Committing;
        await db.SaveChangesAsync(ct);

        var jobId = jobs.Enqueue<MigrationService>(s => s.RunCommitAllCleanAsync(batchId, JobCancellationToken.Null));
        batch.HangfireJobId = jobId;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Commits every not-yet-committed, unambiguous series in a batch — the same "clean"
    /// definition scanning used to auto-commit against before that was removed (see
    /// <see cref="ProcessFolderAsync"/>) — so the (usually large) no-conflict majority can be cleared
    /// in one action after reviewing/fixing the flagged ones. A failure on one series is recorded on
    /// it (<see cref="MigrationSeriesStatus.Failed"/>, error as <see cref="MigrationSeries.ConflictReason"/>)
    /// and doesn't stop the rest. Hangfire job entry point — not declared on <see cref="IMigrationService"/>
    /// (see the comment there), called directly against this concrete class.
    ///
    /// <paramref name="jobToken"/> is Hangfire's cooperative-cancellation hook: it's auto-injected at
    /// execution time (the <c>JobCancellationToken.Null</c> passed at enqueue is just a placeholder for
    /// the compiler), and <see cref="IMigrationService.CancelCommitAllCleanAsync"/> triggers it by
    /// deleting this job id — <c>ThrowIfCancellationRequested</c> below notices between series.</summary>
    [AutomaticRetry(Attempts = 0)]
    public async Task RunCommitAllCleanAsync(Guid batchId, IJobCancellationToken jobToken)
    {
        var ct = jobToken.ShutdownToken;
        var batch = await db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct);
        if (batch is null)
        {
            return;
        }

        try
        {
            var candidates = await db.MigrationSeries
                .Include(s => s.Items)
                .Include(s => s.Batch)
                .Where(s => s.BatchId == batchId && s.Status == MigrationSeriesStatus.NeedsReview)
                .ToListAsync(ct);

            var clean = candidates.Where(IsClean).ToList();
            var seriesDone = 0;
            var seriesTotal = clean.Count;
            batch.CommitSeriesDone = seriesDone;
            batch.CommitSeriesTotal = seriesTotal;
            await db.SaveChangesAsync(ct);
            await ReportBatchProgressAsync(batchId, seriesDone, seriesTotal, ct);

            foreach (var series in clean)
            {
                jobToken.ThrowIfCancellationRequested();
                try
                {
                    await committer.CommitAsync(series, ct);
                }
                catch (OperationCanceledException)
                {
                    throw; // shutdown/abort/cancel — leave this series NeedsReview, don't record it as Failed
                }
                catch (Exception ex)
                {
                    series.Status = MigrationSeriesStatus.Failed;
                    series.ConflictReason = $"Bulk commit failed: {ex.Message}";
                    series.CommitItemsDone = null;
                    series.CommitItemsTotal = null;
                    logger.LogError(ex, "Migration bulk commit failed for series {SeriesId} ({Folder}).",
                        series.Id, series.FolderName);
                }

                seriesDone++;
                batch.CommitSeriesDone = seriesDone;
                await db.SaveChangesAsync(ct);
                await ReportBatchProgressAsync(batchId, seriesDone, seriesTotal, ct);
            }

            batch.Status = MigrationBatchStatus.Done;
            batch.CommitSeriesDone = null;
            batch.CommitSeriesTotal = null;
            batch.HangfireJobId = null;
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Interrupted (shutdown or an admin's Cancel): reset to Done so the batch stays reviewable/
            // retryable rather than stuck in Committing; already-committed series keep their status, the
            // rest stay NeedsReview.
            batch.Status = MigrationBatchStatus.Done;
            batch.CommitSeriesDone = null;
            batch.CommitSeriesTotal = null;
            batch.HangfireJobId = null;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            batch.Status = MigrationBatchStatus.Failed;
            batch.Error = ex.Message;
            batch.CommitSeriesDone = null;
            batch.CommitSeriesTotal = null;
            batch.HangfireJobId = null;
            logger.LogError(ex, "Migration bulk commit failed for batch {BatchId}.", batchId);
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task CancelCommitAllCleanAsync(Guid batchId, CancellationToken ct = default)
    {
        var batch = await db.MigrationBatches.FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new InvalidOperationException("Migration batch not found.");

        if (batch.Status != MigrationBatchStatus.Committing)
        {
            throw new InvalidOperationException("No commit is currently running for this batch.");
        }

        if (batch.HangfireJobId is not null && !jobHealth.IsCrashed(batch.HangfireJobId))
        {
            // Deleting a currently-processing job signals its IJobCancellationToken — the job's own
            // OperationCanceledException handling (above) finishes resetting the batch once it notices,
            // between series rather than instantly, so a cancel while a large series is mid-write doesn't
            // leave it half-written.
            jobs.Delete(batch.HangfireJobId);
            logger.LogInformation("Migration bulk commit for batch {BatchId} cancelled by admin.", batchId);
            return;
        }

        // The job has already crashed (or its id was never recorded) — nothing left to signal, so reset
        // the stuck state directly instead of waiting on a job that isn't coming back.
        batch.Status = MigrationBatchStatus.Done;
        batch.CommitSeriesDone = null;
        batch.CommitSeriesTotal = null;
        batch.HangfireJobId = null;
        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Migration bulk commit for batch {BatchId} had already crashed; reset to Done.", batchId);
    }

    /// <summary>A single-series commit (unlike the bulk one) has no <see cref="MigrationSeriesStatus"/>
    /// value of its own for "committing" — only the batch's status flips to Committing, while the series
    /// keeps whatever status it had and carries <see cref="MigrationSeries.HangfireJobId"/> +
    /// CommitItems* progress for the duration. So "is a single-series commit in flight for this series"
    /// is exactly "HangfireJobId is set".</summary>
    public async Task ResetStuckSeriesCommitAsync(Guid migrationSeriesId, CancellationToken ct = default)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);

        if (migrationSeries.HangfireJobId is null)
        {
            throw new InvalidOperationException("This series has no commit currently in flight.");
        }

        if (!jobHealth.IsCrashed(migrationSeries.HangfireJobId))
        {
            throw new InvalidOperationException(
                "This series' commit job still looks alive — it isn't cancellable while running.");
        }

        migrationSeries.Status = MigrationSeriesStatus.Failed;
        migrationSeries.ConflictReason =
            "Commit job crashed (the app likely restarted mid-commit) — check for partial writes before retrying.";
        migrationSeries.CommitItemsDone = null;
        migrationSeries.CommitItemsTotal = null;
        migrationSeries.HangfireJobId = null;
        // Only the batch's own bulk job (if any) owns clearing Committing when it's the one tracking a
        // job id there; otherwise this single-series commit was what set it, so clear it here too.
        if (migrationSeries.Batch.HangfireJobId is null)
        {
            migrationSeries.Batch.Status = MigrationBatchStatus.Done;
        }

        await db.SaveChangesAsync(ct);
        logger.LogWarning(
            "Migration commit for series {SeriesId} ({Folder}) had already crashed; reset to Failed.",
            migrationSeriesId, migrationSeries.FolderName);
    }

    private async Task ReportBatchProgressAsync(Guid batchId, int seriesDone, int seriesTotal, CancellationToken ct)
    {
        try
        {
            await notifier.MigrationBatchCommitProgressAsync(batchId, seriesDone, seriesTotal, ct);
        }
        catch
        {
            // Live progress is best-effort — the periodic DB persist above is the durable fallback.
        }
    }

    public async Task ClearConflictAsync(Guid migrationSeriesId, CancellationToken ct = default)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        EnsureNotCommitted(migrationSeries);
        EnsureHasMatchingSeries(migrationSeries);

        migrationSeries.ConflictReason = null;
        migrationSeries.ConflictKind = MigrationConflictKind.None;
        await db.SaveChangesAsync(ct);
        logger.LogDebug(
            "Migration review: series {SeriesId} cleared for commit.",
            migrationSeriesId);
    }

    /// <summary>Batch-clears the conflict on every not-yet-committed series in a batch whose <em>only</em>
    /// flagged condition is the partial-purge ranking one — leaves series that also have ambiguous
    /// items, heuristic ties, or a missing opener untouched, since those still need manual review.
    /// Returns how many series were cleared.</summary>
    public async Task<int> ClearRankingOnlyConflictsAsync(Guid batchId, CancellationToken ct = default)
    {
        var targets = await db.MigrationSeries
            .Where(s => s.BatchId == batchId
                        && s.Status != MigrationSeriesStatus.Committed
                        && s.ConflictKind == MigrationConflictKind.PartialPurgeRanking)
            .ToListAsync(ct);

        foreach (var series in targets)
        {
            series.ConflictReason = null;
            series.ConflictKind = MigrationConflictKind.None;
        }

        await db.SaveChangesAsync(ct);
        logger.LogDebug(
            "Migration review: cleared {Count} ranking-only conflict(s) in batch {BatchId}.",
            targets.Count, batchId);
        return targets.Count;
    }

    public async Task RemoveSeriesAsync(Guid migrationSeriesId, CancellationToken ct = default)
    {
        var migrationSeries = await LoadSeriesAsync(migrationSeriesId, ct);
        EnsureNotCommitted(migrationSeries);

        var sourceDir = paths.SeriesInboxFolder(migrationSeries.FolderName);
        if (Directory.Exists(sourceDir))
        {
            // Whole folder, not per-file like MigrationCommitter's outbox moves: nothing here was reviewed
            // item-by-item, so there's no winner/duplicate split to preserve.
            var destDir = UniquePath(Path.Combine(paths.OutboxRoot(migrationSeries.Batch.Kind), migrationSeries.FolderName));
            Directory.Move(sourceDir, destDir);
        }

        db.MigrationSeries.Remove(migrationSeries);
        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Migration review: removed series {SeriesId} ({Folder}) from the batch; its folder was moved to the outbox.",
            migrationSeriesId, migrationSeries.FolderName);
    }

    private static bool IsClean(MigrationSeries series) =>
        series.ConflictReason is null && series.Regime != MigrationRegime.Unmatched;

    // --- internals -------------------------------------------------------------------------------

    private async Task ProcessFolderAsync(MigrationSeries migrationSeries, ScannedSeriesFolder folder, CancellationToken ct)
    {
        var match = await matcher.MatchAsync(folder, ct);
        await ApplyMatchAsync(migrationSeries, match, ct);
        migrationSeries.Status = MigrationSeriesStatus.NeedsReview;

        logger.LogDebug(
            "Migration scan: {Folder} -> needs review ({Reason}).",
            folder.FolderName, migrationSeries.ConflictReason ?? "no conflicts");
    }

    /// <summary>A folder with chapter-shaped files but no ComicInfo.xml anywhere in them almost
    /// certainly isn't from the old MangaDex downloader this tool targets — move it straight into the
    /// import wizard's inbox instead, where it can be scanned/matched there. No <see cref="MigrationSeries"/>
    /// row is created for it; there's nothing to review here. Returns the folder name, for recording on
    /// <see cref="MigrationBatch.DivertedFolders"/>.
    ///
    /// Always the <em>manga</em> import inbox: this tool only ever reads the manga migrate inbox, so
    /// anything it diverts is manga by construction.</summary>
    private string DivertToImportInbox(string sourceDir)
    {
        var folderName = Path.GetFileName(sourceDir);
        var dest = UniquePath(importPaths.SeriesInboxFolder(MediaKind.Manga, folderName));
        Directory.Move(sourceDir, dest);
        logger.LogInformation(
            "Migration scan: {Folder} has no ComicInfo.xml in any file — moved to the manga import inbox instead.",
            folderName);
        return folderName;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileName(path);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(dir, $"{name}-{suffix}");
    }

    private async Task ApplyMatchAsync(MigrationSeries migrationSeries, MatchResult match, CancellationToken ct)
    {
        migrationSeries.ComicInfoSeriesTitle = match.ComicInfoSeriesTitle;
        migrationSeries.MatchedSourceId = match.MatchedSourceId;
        migrationSeries.MatchedSourceSeriesId = match.MatchedSourceSeriesId;
        migrationSeries.MatchedTitle = match.MatchedTitle;
        migrationSeries.Regime = match.Regime;
        migrationSeries.Confidence = match.Confidence;
        migrationSeries.GroupRanking = match.GroupRanking.ToList();
        migrationSeries.ConflictReason = match.ConflictReason;
        migrationSeries.ConflictKind = match.ConflictKind;

        migrationSeries.Items.Clear();
        foreach (var item in match.Items)
        {
            // A matched item is keyed by MangaDex's own number+title (ResolvedNumber/ResolvedTitle), not
            // the local file's, so its NumberKey agrees with the feed-derived Chapter ChapterImporter
            // creates at commit — see the drift explanation in MigrationMatcher.Resolve. Normalize must be
            // called the same way the importer calls it (number + title): a null ResolvedNumber is itself a
            // meaningful matched value (mirrors the feed's own null), and for a numberless oneshot the feed
            // title is what distinguishes its key ("title-<title>") from the bare "oneshot" key — omitting
            // it here is what made a titled oneshot fail to find its imported release at commit.
            var (number, numberKey) = item.MatchedSourceChapterId is not null
                ? (item.ResolvedNumber, ChapterNumber.Normalize(item.ResolvedNumber, title: item.ResolvedTitle).Key)
                : (item.File.Number, item.File.NumberKey);

            var newItem = new MigrationItem
            {
                FileName = item.File.FileName,
                UuidPrefix = item.File.UuidPrefix,
                Number = number,
                NumberKey = numberKey,
                ChapterTitle = item.File.ChapterTitle,
                PageCount = item.File.PageCount,
                SizeBytes = item.File.SizeBytes,
                MatchedSourceChapterId = item.MatchedSourceChapterId,
                MatchedGroup = item.MatchedGroup,
                Disposition = item.Disposition,
                IsWinner = item.IsWinner,
                FlagReason = item.FlagReason,
            };
            migrationSeries.Items.Add(newItem);
            // Force Added state: MigrationItem has a client-set Guid key, and when migrationSeries is
            // an already-tracked (not newly-Added) parent — as it is on rematch — EF's graph tracker
            // can't safely infer that adding to the navigation collection alone means "insert this".
            // Without this, EF sometimes emits an UPDATE for the new row instead of an INSERT, which
            // then fails with DbUpdateConcurrencyException (0 rows affected — the row never existed).
            db.MigrationItems.Add(newItem);
        }

        await DetectMergeTargetAsync(migrationSeries, ct);
    }

    /// <summary>Auto-suggests merging into an existing library series with the same title that isn't
    /// already linked to this MangaDex id — e.g. a hand-created local series. Never overwrites its
    /// metadata; only adds chapters/files on commit. The user can clear this in review.</summary>
    private async Task DetectMergeTargetAsync(MigrationSeries migrationSeries, CancellationToken ct)
    {
        if (migrationSeries.MatchedSourceSeriesId is null)
        {
            migrationSeries.ExistingLibrarySeriesId = null;
            return;
        }

        var alreadyLinked = await db.Series.AnyAsync(
            s => s.Kind == migrationSeries.Batch.Kind
                 && s.SourceLinks.Any(l => l.SourceId == MigrationMatcher.SourceId
                                           && l.SourceSeriesId == migrationSeries.MatchedSourceSeriesId), ct);
        if (alreadyLinked)
        {
            migrationSeries.ExistingLibrarySeriesId = null; // FindOrCreateSeriesAsync will find it by link
            return;
        }

        var candidates = new[] { migrationSeries.MatchedTitle, migrationSeries.ComicInfoSeriesTitle }
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.ToLowerInvariant())
            .Distinct()
            .ToList();

        var titleMatch = await MergeTarget.FindByTitleAsync(db, migrationSeries.Batch.Kind, candidates, ct);

        migrationSeries.ExistingLibrarySeriesId = titleMatch?.Id;
    }

    private ScannedSeriesFolder RescanFolder(string folderName)
    {
        var dir = paths.SeriesInboxFolder(folderName);
        var files = scanner.ScanSeriesFolder(dir);
        return new ScannedSeriesFolder(folderName, dir, files);
    }

    private static MigrationItem ToPendingItem(ScannedFile file) => new()
    {
        FileName = file.FileName,
        UuidPrefix = file.UuidPrefix,
        Number = file.Number,
        NumberKey = file.NumberKey,
        ChapterTitle = file.ChapterTitle,
        PageCount = file.PageCount,
        SizeBytes = file.SizeBytes,
        Disposition = MigrationItemDisposition.Pending,
    };

    // Batch is included because the committer reads its MediaKind to decide which library the series
    // lands in.
    private async Task<MigrationSeries> LoadSeriesAsync(Guid migrationSeriesId, CancellationToken ct) =>
        await db.MigrationSeries.Include(s => s.Items).Include(s => s.Batch)
            .FirstOrDefaultAsync(s => s.Id == migrationSeriesId, ct)
        ?? throw new InvalidOperationException("Migration series not found.");

    private static void EnsureNotCommitted(MigrationSeries series)
    {
        if (series.Status == MigrationSeriesStatus.Committed)
        {
            throw new InvalidOperationException("This series has already been committed.");
        }
    }

    private static void EnsureHasMatchingSeries(MigrationSeries series)
    {
        if (series.MatchedSourceSeriesId == null)
        {
            throw new InvalidOperationException("This entry has no matching source series id.");
        }
    }

    private MigrationBatchDetail ToDetail(MigrationBatch batch) => new(
        batch.Id, batch.CreatedAt, batch.Status.ToString(), batch.Error,
        batch.DivertedFolders, batch.Series.Select(ToDetail).ToList(),
        batch.CommitSeriesDone, batch.CommitSeriesTotal,
        batch.Status == MigrationBatchStatus.Committing && jobHealth.IsCrashed(batch.HangfireJobId));

    private MigrationSeriesDetail ToDetail(MigrationSeries s) => new(
        s.Id, s.FolderName, s.ComicInfoSeriesTitle, s.MatchedSourceSeriesId, s.MatchedTitle,
        s.Regime.ToString(), s.Confidence, s.Status.ToString(), s.ConflictReason,
        s.ConflictKind == MigrationConflictKind.PartialPurgeRanking,
        s.ExistingLibrarySeriesId, s.CommittedLibrarySeriesId, s.GroupRanking,
        s.Items.OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase).Select(ToDetail).ToList(),
        s.CommitItemsDone, s.CommitItemsTotal,
        s.HangfireJobId is not null && jobHealth.IsCrashed(s.HangfireJobId));

    private static MigrationItemDetail ToDetail(MigrationItem i) => new(
        i.Id, i.FileName, i.UuidPrefix, i.Number, i.ChapterTitle, i.PageCount, i.SizeBytes,
        i.MatchedGroup, i.Disposition.ToString(), i.IsWinner, i.FlagReason);
}
