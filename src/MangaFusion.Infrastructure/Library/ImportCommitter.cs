using MangaFusion.Application.Library;
using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Commits one reviewed <see cref="ImportSeries"/>: creates or merges the library series
/// (applying MangaUpdates metadata if matched — never overwriting an existing merge target's), tags it
/// as a "local" content source alongside its "mangaupdates" metadata link, then imports each included
/// item as a chapter via <see cref="ChapterFileImporter"/> (converting PDFs along the way) and removes
/// the imported files from the inbox. Nothing on disk moves until this runs. Reports live progress —
/// item-level always, page-level while rasterizing a PDF (the slow part, easily minutes) — over
/// <see cref="ILibraryNotifier"/>, the same channel download progress already uses.</summary>
public sealed class ImportCommitter(
    AppDbContext db, ILibraryService libraryService, ImportPaths paths, ChapterFileImporter chapterImporter,
    ProseChapterImporter proseImporter, ILibraryNotifier notifier, ILogger<ImportCommitter> logger)
{
    private const string DefaultLanguage = "en";

    public async Task<Guid> CommitAsync(ImportSeries importSeries, CancellationToken ct)
    {
        var included = importSeries.Items.Where(i => i.Include).ToList();
        if (included.Count == 0)
        {
            throw new InvalidOperationException("No items are included for import.");
        }

        // Everything an earlier attempt already landed. Its chapters exist and its source files are gone,
        // so this run must skip it: re-importing is impossible (nothing left to read) and it would
        // collide with the very chapters it created. This is what makes a retry a resume.
        var pending = included.Where(i => i.ImportedAt is null).ToList();

        var isMerge = importSeries.ExistingLibrarySeriesId is not null;
        logger.LogDebug(
            "Import commit: {Title} -> {Mode}, {PendingCount} of {ItemCount} item(s) still to import.",
            importSeries.GroupTitle,
            isMerge ? $"merge into {importSeries.ExistingLibrarySeriesId}" : "new/linked series",
            pending.Count,
            included.Count);

        var series = await ResolveSeriesAsync(importSeries, isMerge, ct);

        // A title override (e.g. picking a MangaUpdates alt-title over its primary title) only makes
        // sense for a series whose metadata we actually just applied — never for a merge target, whose
        // existing title is deliberately left untouched.
        if (!isMerge && !string.IsNullOrWhiteSpace(importSeries.TitleOverride))
        {
            series.Title = importSeries.TitleOverride.Trim();
        }

        // Validate the chapter-number keys up front, before anything on disk moves or any chapter is
        // created — a collision discovered mid-loop leaves a partially-imported series. Only the pending
        // items are checked: an already-imported one is *supposed* to match the chapter it created.
        EnsureNoNumberCollisions(series, pending, DefaultLanguage);

        EnsureLocalSourceLink(series);

        var itemsTotal = included.Count;
        var itemsDone = itemsTotal - pending.Count; // resumed items count as done for progress
        importSeries.CommitItemsDone = itemsDone;
        importSeries.CommitItemsTotal = itemsTotal;
        importSeries.CommitPageDone = null;
        importSeries.CommitPageTotal = null;
        await db.SaveChangesAsync(ct);
        await ReportAsync(importSeries.Id, itemsDone, itemsTotal, null, null, ct);

        var batchKind = importSeries.Batch.Kind;
        var inboxRoot = paths.InboxRoot(batchKind);
        foreach (var item in pending)
        {
            var releaseDir = paths.SeriesInboxFolder(batchKind, item.FolderName);
            // FileName is relative to the release folder and may include subfolder segments (a
            // release's chapter files can sit below the top-level release folder, not directly in
            // it) — "" means the release folder itself is the folder-of-images source.
            var sourcePath = Path.Combine(releaseDir, item.FileName);
            var fileBaseName = LibraryPaths.Sanitize(Path.GetFileNameWithoutExtension(sourcePath));
            var specs = new List<LocalChapterSpec> { new(item.Number, item.Volume, item.Title, 0) };

            // Only a PDF is slow enough to need page-level progress. The live pushes below never touch
            // `db` — same reason DownloadOrchestrator's parallel page loop doesn't: this is the sole
            // DbContext user for the whole commit, so nothing else may write concurrently with it.
            var pageTotal = item.Format == ImportSourceFormat.Pdf ? item.PageCount : (int?)null;
            importSeries.CommitPageDone = pageTotal is null ? null : 0;
            importSeries.CommitPageTotal = pageTotal;
            await db.SaveChangesAsync(ct);
            await ReportAsync(importSeries.Id, itemsDone, itemsTotal, importSeries.CommitPageDone, pageTotal, ct);

            IProgress<int>? pageProgress = pageTotal is null
                ? null
                : new RelayProgress<int>(done => _ = ReportAsync(importSeries.Id, itemsDone, itemsTotal, done, pageTotal, CancellationToken.None));

            var sourceKind = ToSourceKind(item.Format);
            if (ChapterSourceKindClassifier.IsProse(sourceKind))
            {
                // Prose commits into an EPUB3 text artifact via the parallel importer (a text/mixed EPUB is
                // stored as-is; text/PDF/txt/md are wrapped) — no rasterization, so no page-level progress.
                await proseImporter.ImportAsync(
                    series, sourcePath, sourceKind, fileBaseName, DefaultLanguage, specs, ct);
            }
            else
            {
                await chapterImporter.ImportAsync(
                    series, sourcePath, sourceKind, fileBaseName, DefaultLanguage, specs, ct, pageProgress);
            }

            // Mark the item imported the moment its chapter is durably in the DB, and *before* the source
            // file is removed. Ordered this way, a crash in the gap leaves a stray inbox file (harmless —
            // the next scan just re-offers it) instead of an item whose chapter exists but whose source is
            // gone, which no retry could ever resolve.
            itemsDone++;
            item.ImportedAt = DateTimeOffset.UtcNow;
            importSeries.CommitItemsDone = itemsDone;
            importSeries.CommitPageDone = pageTotal;
            await db.SaveChangesAsync(ct);

            if (item.Format == ImportSourceFormat.Folder)
            {
                if (Directory.Exists(sourcePath))
                {
                    Directory.Delete(sourcePath, recursive: true);
                }
            }
            else if (File.Exists(sourcePath))
            {
                File.Delete(sourcePath);
            }

            // Cascade upward removing now-empty directories (a subfolder the file/folder sat in, then
            // its parent, and so on) — up to but not including the shared inbox root — so a fully-
            // imported release doesn't linger and get re-offered on the next scan. Stops as soon as a
            // directory still has something in it (e.g. an excluded sibling file).
            RemoveEmptyAncestors(Path.GetDirectoryName(sourcePath)!, inboxRoot);

            await ReportAsync(importSeries.Id, itemsDone, itemsTotal, importSeries.CommitPageDone, pageTotal, ct);
        }

        importSeries.Status = ImportSeriesStatus.Committed;
        importSeries.CommitItemsDone = null;
        importSeries.CommitItemsTotal = null;
        importSeries.CommitPageDone = null;
        importSeries.CommitPageTotal = null;
        importSeries.CommitError = null;
        await db.SaveChangesAsync(ct);
        await ReportAsync(importSeries.Id, itemsTotal, itemsTotal, null, null, ct, "Committed");
        logger.LogDebug("Import commit: {Title} committed to series {SeriesId}.", importSeries.GroupTitle, series.Id);

        return series.Id;
    }

    private async Task ReportAsync(
        Guid importSeriesId, int itemsDone, int itemsTotal, int? pageDone, int? pageTotal, CancellationToken ct,
        string status = "Committing")
    {
        try
        {
            await notifier.ImportCommitProgressAsync(importSeriesId, status, itemsDone, itemsTotal, pageDone, pageTotal, ct);
        }
        catch
        {
            // Live progress is best-effort — the periodic DB persist above is the durable fallback.
        }
    }

    /// <summary>Adapts a plain callback to <see cref="IProgress{T}"/> without pulling in a
    /// SynchronizationContext-dependent implementation — <see cref="Progress{T}"/> would post the
    /// callback back onto whatever context captured it at construction time (the ASP.NET request or
    /// Hangfire worker context), which isn't guaranteed to still be around/relevant by the time PDF
    /// rendering (a background thread-pool loop) reports progress.</summary>
    private sealed class RelayProgress<T>(Action<T> onReport) : IProgress<T>
    {
        public void Report(T value) => onReport(value);
    }

    /// <summary>Throws if any two of the <paramref name="pending"/> items would key to the same chapter
    /// number, or if one collides with a chapter the target series already has — both kinds of collision
    /// otherwise surface as a DB-level unique-constraint failure, but only after earlier items in the
    /// batch have already been imported and their source files deleted.
    ///
    /// Only pending items are checked. An already-imported item's key *does* match an existing chapter —
    /// the one it created — so including it here would make every resume fail on its own prior work.</summary>
    private static void EnsureNoNumberCollisions(
        Series series, IReadOnlyList<ImportItem> pending, string language)
    {
        var existingKeys = series.Chapters
            .Where(c => c.Language == language)
            .Select(c => c.NumberKey)
            .ToHashSet();

        var seen = new Dictionary<string, ImportItem>();
        foreach (var item in pending)
        {
            var key = ChapterNumber.QualifyKey(series.SortMode, ChapterNumber.Normalize(item.Number, item.Volume).Key, item.Volume);
            if (existingKeys.Contains(key))
            {
                throw new InvalidOperationException(
                    $"\"{item.FileName}\" (number \"{item.Number ?? "(blank)"}\") would collide with a chapter " +
                    $"'{key}' that already exists in this series. Give it a distinct number before committing.");
            }

            if (seen.TryGetValue(key, out var other))
            {
                var bothWholeVolume = item.Number is null && other.Number is null;
                throw new InvalidOperationException(
                    $"\"{item.FileName}\" and \"{other.FileName}\" both resolve to chapter number '{key}' " +
                    (bothWholeVolume && item.Volume is null && other.Volume is null
                        ? "(both are blank, which collapses to the same \"oneshot\" chapter)"
                        : bothWholeVolume
                            ? "(both are whole-volume imports for the same volume — give them distinct volumes)"
                            : "— give them distinct numbers") + " before committing.");
            }

            seen[key] = item;
        }
    }

    /// <summary>The library series this import commits into, resolved at most once. Re-resolving on a
    /// retry would be wrong: <see cref="CreateUnmatchedSeriesAsync"/> creates unconditionally, so a
    /// second attempt at an unmatched import would leave an orphaned, empty series behind and land its
    /// remaining chapters in a different series than its first attempt did. So the resolved id is
    /// recorded on the ImportSeries as soon as it's known, and a resume reuses it.</summary>
    private async Task<Series> ResolveSeriesAsync(ImportSeries importSeries, bool isMerge, CancellationToken ct)
    {
        var seriesId = importSeries.CommittedLibrarySeriesId
            ?? (isMerge
                ? await MergeIntoExistingAsync(importSeries, ct)
                : importSeries.MatchedSourceSeriesId is { } sourceSeriesId
                    // createKind: the batch's library (the user's mode + per-kind inbox) is authoritative —
                    // a MangaUpdates match supplies metadata, it doesn't get to land a light-novel import in
                    // the manga library just because its type string wasn't exactly "Novel".
                    ? await libraryService.AddOrUpdateMetadataOnlyAsync(
                        ImportMatcher.SourceFor(importSeries.Batch.Kind), sourceSeriesId, importSeries.Batch.Kind, ct)
                    : await CreateUnmatchedSeriesAsync(importSeries.GroupTitle, importSeries.Batch.Kind, ct));

        if (importSeries.CommittedLibrarySeriesId != seriesId)
        {
            importSeries.CommittedLibrarySeriesId = seriesId;
            await db.SaveChangesAsync(ct);
        }

        return await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Chapters)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .Include(s => s.Tags)
            .FirstAsync(s => s.Id == seriesId, ct);
    }

    private async Task<Guid> MergeIntoExistingAsync(ImportSeries importSeries, CancellationToken ct)
    {
        var existingId = importSeries.ExistingLibrarySeriesId!.Value;

        // Re-checked here, not just where the target was chosen: the series could have been picked before a
        // guard existed, or changed underneath us. Committing into the other library writes this batch's
        // files under the wrong root, which is not something a later scan can detect or undo.
        await MergeTarget.EnsureInLibraryAsync(db, existingId, importSeries.Batch.Kind, ct);

        if (importSeries.MatchedSourceSeriesId is { } sourceSeriesId)
        {
            var series = await db.Series.Include(s => s.SourceLinks).FirstOrDefaultAsync(s => s.Id == existingId, ct)
                ?? throw new InvalidOperationException("Merge target series not found.");

            // Never overwrite an existing series' metadata on merge — only add the cross-reference.
            var matchSourceId = ImportMatcher.SourceFor(importSeries.Batch.Kind);
            var hasLink = series.SourceLinks.Any(l =>
                l.SourceId == matchSourceId && l.SourceSeriesId == sourceSeriesId);
            if (!hasLink)
            {
                var link = new SeriesSourceLink
                {
                    SourceId = matchSourceId,
                    SourceSeriesId = sourceSeriesId,
                    Kind = series.Kind,
                    IsMetadataPrimary = false,
                };
                series.SourceLinks.Add(link);
                // Force Added state: series is an already-tracked (not newly-Added) parent here, so EF
                // can't safely infer that adding to the navigation collection alone means insert (same
                // client-set-Guid-key pitfall as MigrationService.ApplyMatchAsync's MigrationItem adds).
                db.Add(link);
                await db.SaveChangesAsync(ct);
            }
        }
        else if (!await db.Series.AnyAsync(s => s.Id == existingId, ct))
        {
            throw new InvalidOperationException("Merge target series not found.");
        }

        return existingId;
    }

    private async Task<Guid> CreateUnmatchedSeriesAsync(string title, MediaKind kind, CancellationToken ct)
    {
        var series = new Series { Title = title, Kind = kind };
        series.SourceLinks.Add(new SeriesSourceLink
        {
            SourceId = LocalSourceConstants.SourceId,
            SourceSeriesId = Guid.NewGuid().ToString("N"),
            Kind = kind,
            IsMetadataPrimary = true,
        });
        db.Series.Add(series);
        await db.SaveChangesAsync(ct);
        return series.Id;
    }

    private void EnsureLocalSourceLink(Series series)
    {
        var hasLocal = series.SourceLinks.Any(l => l.SourceId == LocalSourceConstants.SourceId);
        if (hasLocal)
        {
            return;
        }

        var link = new SeriesSourceLink
        {
            SourceId = LocalSourceConstants.SourceId,
            SourceSeriesId = Guid.NewGuid().ToString("N"),
            Kind = series.Kind,
            IsMetadataPrimary = false,
        };
        series.SourceLinks.Add(link);
        // Force Added state — see the identical note in MergeIntoExistingAsync.
        db.Add(link);
    }

    /// <summary>Deletes <paramref name="dir"/> and walks upward deleting each now-empty parent, until
    /// hitting <paramref name="stopAt"/> (exclusive — never deleted) or a directory that still has
    /// something in it.</summary>
    private static void RemoveEmptyAncestors(string dir, string stopAt)
    {
        var current = dir;
        while (!string.Equals(current, stopAt, StringComparison.Ordinal)
               && Directory.Exists(current)
               && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            var parent = Path.GetDirectoryName(current);
            Directory.Delete(current);
            if (parent is null)
            {
                break;
            }

            current = parent;
        }
    }

    private static ChapterSourceKind ToSourceKind(ImportSourceFormat format) => format switch
    {
        ImportSourceFormat.Cbz => ChapterSourceKind.Cbz,
        ImportSourceFormat.Folder => ChapterSourceKind.Folder,
        ImportSourceFormat.Pdf => ChapterSourceKind.Pdf,
        ImportSourceFormat.Cbr => ChapterSourceKind.Cbr,
        ImportSourceFormat.Epub => ChapterSourceKind.Epub,
        ImportSourceFormat.ProseEpub => ChapterSourceKind.ProseEpub,
        ImportSourceFormat.ProsePdf => ChapterSourceKind.ProsePdf,
        ImportSourceFormat.ProseText => ChapterSourceKind.ProseText,
        ImportSourceFormat.ProseMarkdown => ChapterSourceKind.ProseMarkdown,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
