using System.IO.Compression;
using MangaFusion.Application.Library;
using MangaFusion.Application.Realtime;
using MangaFusion.Application.Sources;
using MangaFusion.Application.Writing;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SourceChapter = MangaFusion.Contracts.Models.SourceChapter;
using ChapterQuery = MangaFusion.Contracts.Models.ChapterQuery;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Commits one reviewed (or auto-clean) <see cref="MigrationSeries"/>: creates or merges the
/// library series, imports the full source feed (so purged series still get whatever the feed still
/// knows — e.g. external retail stubs — exactly like a normal add-to-library), then overlays the
/// winning local files as artifacts and moves every other file to the outbox. Nothing on disk moves
/// until this runs. Winning files also get their pages re-encoded in place via
/// <see cref="ArtifactPageReencoder"/> (lossless WebP where that shrinks them), same as freshly
/// downloaded/imported chapters.</summary>
public sealed class MigrationCommitter(
    AppDbContext db, ISourceRegistry registry, ChapterImporter importer, LibraryPaths paths,
    MigrationPaths migrationPaths, SeriesMetadataApplier metadataApplier, SeriesCoverCache coverCache,
    ArtifactFileInspector artifactInspector, ArtifactPageReencoder reencoder, ILibraryNotifier notifier,
    ILogger<MigrationCommitter> logger)
{
    private const int FeedPageSize = 500;
    private const int MaxFeedPages = 20;

    public async Task<Guid> CommitAsync(MigrationSeries migrationSeries, CancellationToken ct)
    {
        if (migrationSeries.Items.Any(i =>
                i.Disposition is MigrationItemDisposition.Pending or MigrationItemDisposition.Unresolved))
        {
            throw new InvalidOperationException("This series still has unresolved items.");
        }

        var isMerge = migrationSeries.ExistingLibrarySeriesId is not null;
        logger.LogDebug(
            "Migration commit: {Folder} -> {Mode}, {ImportCount} winner(s), {OutboxCount} to outbox.",
            migrationSeries.FolderName,
            isMerge ? $"merge into {migrationSeries.ExistingLibrarySeriesId}" : "new/linked series",
            migrationSeries.Items.Count(i => i.Disposition == MigrationItemDisposition.Import),
            migrationSeries.Items.Count(i => i.Disposition is MigrationItemDisposition.Duplicate or MigrationItemDisposition.Quarantine));

        if (isMerge)
        {
            // Re-checked at commit, not just where the target was chosen — merging into the other library
            // writes this batch's files under the wrong root, which no later scan can detect or undo.
            await MergeTarget.EnsureInLibraryAsync(
                db, migrationSeries.ExistingLibrarySeriesId!.Value, migrationSeries.Batch.Kind, ct);
        }

        var series = isMerge
            ? await LoadMergeTargetAsync(migrationSeries.ExistingLibrarySeriesId!.Value, ct)
            : await FindOrCreateSeriesAsync(migrationSeries, ct);

        if (migrationSeries.MatchedSourceSeriesId is not null)
        {
            if (!isMerge)
            {
                await ApplyMetadataAsync(series, migrationSeries.MatchedSourceSeriesId, ct);
            }

            EnsureSourceLink(series, migrationSeries.MatchedSourceSeriesId, isPrimary: !isMerge);

            var chapterSource = registry.GetChapterSource(MigrationMatcher.SourceId);
            var feed = await FetchAllChaptersAsync(chapterSource, migrationSeries.MatchedSourceSeriesId, ct);
            logger.LogDebug(
                "Migration commit: importing {FeedCount} feed chapter(s) into {Series}.", feed.Count, series.Title);
            await importer.ImportAsync(series, feed, ct);
            series.LastScannedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct); // chapters/releases must exist before we can point artifacts at them

        var winners = migrationSeries.Items.Where(i => i.Disposition == MigrationItemDisposition.Import).ToList();
        var itemsDone = 0;
        var itemsTotal = winners.Count;
        migrationSeries.CommitItemsDone = itemsDone;
        migrationSeries.CommitItemsTotal = itemsTotal;
        await db.SaveChangesAsync(ct);
        await ReportAsync(migrationSeries.Id, itemsDone, itemsTotal, ct);

        var pendingActivePointers = new List<(Chapter Chapter, Artifact Artifact, ChapterRelease Release)>();
        foreach (var item in winners)
        {
            var pointer = await ImportWinnerAsync(series, migrationSeries, item, ct);
            if (pointer is not null)
            {
                pendingActivePointers.Add(pointer.Value);
            }

            itemsDone++;
            migrationSeries.CommitItemsDone = itemsDone;
            await db.SaveChangesAsync(ct);
            await ReportAsync(migrationSeries.Id, itemsDone, itemsTotal, ct);
        }

        foreach (var item in migrationSeries.Items.Where(i =>
                     i.Disposition is MigrationItemDisposition.Duplicate or MigrationItemDisposition.Quarantine))
        {
            MoveToOutbox(series.Kind, migrationSeries.FolderName, item.FileName);
        }

        if (migrationSeries.GroupRanking.Count > 0)
        {
            series.PreferredGroups = migrationSeries.GroupRanking;
        }

        await db.SaveChangesAsync(ct); // insert new chapters/releases/artifacts first (avoids an FK cycle)
        foreach (var (chapter, artifact, release) in pendingActivePointers)
        {
            chapter.ActiveArtifactId = artifact.Id;
            chapter.ActiveReleaseId = release.Id;
        }

        migrationSeries.Status = MigrationSeriesStatus.Committed;
        migrationSeries.CommittedLibrarySeriesId = series.Id;
        migrationSeries.ConflictReason = null;
        migrationSeries.ConflictKind = MigrationConflictKind.None;
        migrationSeries.CommitItemsDone = null;
        migrationSeries.CommitItemsTotal = null;
        await db.SaveChangesAsync(ct);
        await ReportAsync(migrationSeries.Id, itemsTotal, itemsTotal, ct, "Committed");
        logger.LogDebug("Migration commit: {Folder} committed to series {SeriesId}.", migrationSeries.FolderName, series.Id);

        RemoveInboxFolderIfEmpty(migrationSeries.FolderName);

        return series.Id;
    }

    private async Task ReportAsync(
        Guid migrationSeriesId, int itemsDone, int itemsTotal, CancellationToken ct, string status = "Committing")
    {
        try
        {
            await notifier.MigrationCommitProgressAsync(migrationSeriesId, status, itemsDone, itemsTotal, ct);
        }
        catch
        {
            // Live progress is best-effort — the periodic DB persist above is the durable fallback.
        }
    }

    /// <summary>Every file is either imported or moved to the outbox by this point — if that leaves
    /// the inbox subfolder empty, drop it too, so a fully-migrated series doesn't linger there.
    /// Leaves it in place if anything unrecognized (e.g. a stray non-chapter file) remains.</summary>
    private void RemoveInboxFolderIfEmpty(string folderName)
    {
        var dir = migrationPaths.SeriesInboxFolder(folderName);
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            logger.LogDebug("Migration commit: removed now-empty inbox folder {Folder}.", folderName);
        }
    }

    private async Task<Series> FindOrCreateSeriesAsync(MigrationSeries migrationSeries, CancellationToken ct)
    {
        var sourceSeriesId = migrationSeries.MatchedSourceSeriesId
            ?? throw new InvalidOperationException("Series has no MangaDex match to commit against.");

        var existing = await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Tags)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .FirstOrDefaultAsync(
                s => s.SourceLinks.Any(l => l.SourceId == MigrationMatcher.SourceId && l.SourceSeriesId == sourceSeriesId),
                ct);
        if (existing is not null)
        {
            return existing;
        }

        var created = new Series { Kind = migrationSeries.Batch.Kind };
        db.Series.Add(created);
        return created;
    }

    private async Task<Series> LoadMergeTargetAsync(Guid seriesId, CancellationToken ct) =>
        await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Tags)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .Include(s => s.Chapters).ThenInclude(c => c.Releases)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
        ?? throw new InvalidOperationException("Merge target series not found.");

    private async Task ApplyMetadataAsync(Series series, string sourceSeriesId, CancellationToken ct)
    {
        var metadata = registry.GetMetadataSource(MigrationMatcher.SourceId);
        var sourceSeries = await metadata.GetSeriesAsync(sourceSeriesId, ct);
        if (sourceSeries is null)
        {
            return;
        }

        await metadataApplier.ApplyAsync(series, sourceSeries, ct);
        await coverCache.TryCacheAsync(series, sourceSeries.CoverUrl, ct);
    }

    private static void EnsureSourceLink(Series series, string sourceSeriesId, bool isPrimary)
    {
        var hasLink = series.SourceLinks.Any(l =>
            l.SourceId == MigrationMatcher.SourceId && l.SourceSeriesId == sourceSeriesId);
        if (hasLink)
        {
            return;
        }

        series.SourceLinks.Add(new SeriesSourceLink
        {
            SourceId = MigrationMatcher.SourceId,
            SourceSeriesId = sourceSeriesId,
            IsMetadataPrimary = isPrimary,
        });
    }

    /// <summary>Moves a winning file into the library and wires it up as the chapter's active
    /// artifact. Returns the (chapter, artifact, release) triple to point at once everything is
    /// inserted — assigning <c>ActiveReleaseId</c> before the release is saved trips EF's cycle
    /// detection, same as in <see cref="LocalImportService"/>.</summary>
    private async Task<(Chapter, Artifact, ChapterRelease)?> ImportWinnerAsync(
        Series series, MigrationSeries migrationSeries, MigrationItem item, CancellationToken ct)
    {
        var chapter = await FindOrCreateChapterAsync(series, item, ct);
        var release = await FindOrCreateReleaseAsync(chapter, item, ct);

        var sourcePath = migrationPaths.SeriesInboxFolder(migrationSeries.FolderName);
        var format = item.FileName.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase)
            ? StorageFormat.Cbz
            : StorageFormat.Folder;
        var (destPath, relativePath) = MoveIntoLibrary(
            series.Kind, series.Title, Path.Combine(sourcePath, item.FileName), format);
        await reencoder.ReencodeAsync(destPath, format, ct);
        await RewriteComicInfoAsync(series, release, item, destPath, format, ct);

        var artifact = new Artifact
        {
            SeriesId = series.Id,
            Format = format,
            Origin = item.MatchedSourceChapterId is not null ? ArtifactOrigin.Download : ArtifactOrigin.Local,
            Path = relativePath,
            PageCount = item.PageCount,
            SizeBytes = format == StorageFormat.Cbz ? new FileInfo(destPath).Length : DirSize(destPath),
            Hash = await artifactInspector.HashAsync(destPath, format, ct),
            Status = ArtifactStatus.Complete,
        };
        db.Artifacts.Add(artifact);
        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });

        return (chapter, artifact, release);
    }

    /// <summary>Replaces the archive's embedded ComicInfo.xml with one built from the library's own
    /// data. The old downloader's copy has known gaps this fixes: no scanlation group at all, and
    /// Year/Month/Day set from the manga's creation date rather than the chapter's. Only the metadata
    /// entry is touched here — page images are already handled separately by
    /// <see cref="ArtifactPageReencoder"/>, called just before this in <see cref="ImportWinnerAsync"/>.</summary>
    private async Task RewriteComicInfoAsync(
        Series series, ChapterRelease release, MigrationItem item, string destPath, StorageFormat format,
        CancellationToken ct)
    {
        var group = release.GroupKey ?? (release.ScanlationGroups.Count > 0 ? release.ScanlationGroups[0] : null);
        var segment = new ChapterSegment(
            item.Number, Volume: null, item.ChapterTitle, "en", group, Pages: [],
            release.PublishedAt, BuildSourceUrl(release.SourceId, release.SourceChapterId));

        var request = new WriteRequest(
            series.Title,
            series.Authors.Select(a => a.Name).ToList(),
            series.Tags.Where(t => t.Group == "genre").Select(t => t.Name).ToList(),
            format,
            TargetDirectory: "",
            FileBaseName: "",
            [segment],
            Artists: series.Artists.Select(a => a.Name).ToList(),
            OtherTags: series.Tags.Where(t => t.Group != "genre").Select(t => t.Name).ToList(),
            Description: series.Description,
            ContentRating: series.ContentRating,
            OriginalLanguage: series.OriginalLanguage,
            AltTitles: series.AltTitles,
            Kind: series.Kind);

        if (format == StorageFormat.Cbz)
        {
            using var zip = ZipFile.Open(destPath, ZipArchiveMode.Update);
            var existing = zip.Entries.FirstOrDefault(
                e => string.Equals(e.Name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
            existing?.Delete();

            await using var entryStream = zip.CreateEntry("ComicInfo.xml").Open();
            await ComicInfoXml.WriteAsync(entryStream, request, item.PageCount, ct);
        }
        else
        {
            await using var fileStream = File.Create(Path.Combine(destPath, "ComicInfo.xml"));
            await ComicInfoXml.WriteAsync(fileStream, request, item.PageCount, ct);
        }
    }

    private static string? BuildSourceUrl(string sourceId, string sourceChapterId) =>
        sourceId == MigrationMatcher.SourceId ? $"https://mangadex.org/chapter/{sourceChapterId}" : null;

    private async Task<Chapter> FindOrCreateChapterAsync(Series series, MigrationItem item, CancellationToken ct)
    {
        // Explicit Include even though ChapterImporter (same DbContext, run just before this) already
        // populates Releases in-memory for chapters it created — a chapter surviving from an earlier
        // migration commit or normal usage is tracked fresh here and needs it loaded for
        // FindOrCreateReleaseAsync's in-memory lookup to see its existing releases.
        var existing = await db.Chapters
            .Include(c => c.Releases)
            .FirstOrDefaultAsync(c => c.SeriesId == series.Id && c.Language == "en" && c.NumberKey == item.NumberKey, ct);
        if (existing is not null)
        {
            return existing;
        }

        var (sort, key) = ChapterNumber.Normalize(item.Number);
        var chapter = new Chapter
        {
            SeriesId = series.Id,
            Language = "en",
            Number = item.Number,
            NumberSort = sort,
            NumberKey = key,
            Title = item.ChapterTitle,
        };
        series.Chapters.Add(chapter);
        db.Chapters.Add(chapter); // force Added state (entity carries a client-set Guid key)
        return chapter;
    }

    private async Task<ChapterRelease> FindOrCreateReleaseAsync(Chapter chapter, MigrationItem item, CancellationToken ct)
    {
        if (item.MatchedSourceChapterId is not null)
        {
            // ChapterImporter (run earlier in CommitAsync, same DbContext) already created this
            // release when it imported the feed — the chapter it attached to came back out of EF's
            // identity map via FindOrCreateChapterAsync, so its in-memory Releases are populated.
            var release = chapter.Releases.FirstOrDefault(r =>
                r.SourceId == MigrationMatcher.SourceId && r.SourceChapterId == item.MatchedSourceChapterId);
            if (release is not null)
            {
                return release;
            }

            throw new InvalidOperationException(
                $"Matched release '{item.MatchedSourceChapterId}' was not found after importing the feed " +
                "— it may have been renumbered or removed on MangaDex between scan and commit.");
        }

        var local = new ChapterRelease
        {
            SourceId = LocalSourceConstants.SourceId,
            SourceChapterId = $"{Guid.NewGuid():N}:0",
            ScanlationGroups = [],
            GroupKey = null,
            PublishedAt = DateTimeOffset.UtcNow,
            PageCount = item.PageCount,
            IsExternal = false,
        };
        chapter.Releases.Add(local);
        db.ChapterReleases.Add(local);
        return local;
    }

    private (string DestPath, string RelativePath) MoveIntoLibrary(
        MediaKind kind, string seriesTitle, string sourcePath, StorageFormat format)
    {
        var dir = paths.SeriesDirectory(kind, seriesTitle);
        Directory.CreateDirectory(dir);

        var baseName = LibraryPaths.Sanitize(Path.GetFileNameWithoutExtension(sourcePath));
        if (format == StorageFormat.Cbz)
        {
            var dest = LibraryPaths.UniquePath(Path.Combine(dir, baseName + ".cbz"));
            File.Move(sourcePath, dest);
            return (dest, paths.RelativeTo(kind, dest));
        }

        var destDir = LibraryPaths.UniquePath(Path.Combine(dir, baseName));
        Directory.Move(sourcePath, destDir);
        return (destDir, paths.RelativeTo(kind, destDir));
    }

    private void MoveToOutbox(MediaKind kind, string folderName, string fileName)
    {
        var sourcePath = Path.Combine(migrationPaths.SeriesInboxFolder(folderName), fileName);
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            return; // already moved (e.g. re-running commit after a partial failure)
        }

        var destDir = migrationPaths.SeriesOutboxFolder(kind, folderName);
        var dest = LibraryPaths.UniquePath(Path.Combine(destDir, fileName));
        if (File.Exists(sourcePath))
        {
            File.Move(sourcePath, dest);
        }
        else
        {
            Directory.Move(sourcePath, dest);
        }
    }

    private static long DirSize(string path) =>
        Directory.EnumerateFiles(path).Sum(f => new FileInfo(f).Length);

    private static async Task<List<SourceChapter>> FetchAllChaptersAsync(
        IChapterSource source, string sourceSeriesId, CancellationToken ct)
    {
        var all = new List<SourceChapter>();
        var offset = 0;
        for (var page = 0; page < MaxFeedPages; page++)
        {
            var result = await source.GetChaptersAsync(
                sourceSeriesId, new ChapterQuery { Limit = FeedPageSize, Offset = offset, IncludeExternal = true }, ct);

            all.AddRange(result.Items);
            offset += FeedPageSize;
            if (result.Items.Count < FeedPageSize || offset >= result.Total)
            {
                break;
            }
        }

        return all;
    }
}
