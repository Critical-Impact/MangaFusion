using System.Diagnostics;
using Hangfire;
using MangaFusion.Application.Notifications;
using MangaFusion.Application.Realtime;
using MangaFusion.Application.Sources;
using MangaFusion.Application.Writing;
using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Writing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Downloads;

/// <summary>Runs a single-release download as a Hangfire job: resolve pages, fetch images (bounded
/// concurrency, with MangaDex@Home reporting), write the artifact, and link it to the chapter.</summary>
public sealed class DownloadOrchestrator(
    AppDbContext db,
    ISourceRegistry registry,
    ChapterWriterSelector writers,
    LibraryPaths paths,
    IHttpClientFactory httpFactory,
    ILibraryNotifier notifier,
    INotificationService notifications,
    ILogger<DownloadOrchestrator> logger)
{
    public const string ImageClientName = "download-images";
    private const int MaxConcurrentPages = 4;

    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(Guid downloadId, CancellationToken ct)
    {
        var download = await db.Downloads.FirstOrDefaultAsync(d => d.Id == downloadId, ct);
        if (download is null)
        {
            logger.LogWarning("Download {Id} not found; skipping.", downloadId);
            return;
        }

        try
        {
            download.Status = DownloadStatus.Running;
            await db.SaveChangesAsync(ct);
            await notifier.DownloadProgressAsync(download.Id, download.ChapterId, DownloadStatus.Running, 0, download.PagesTotal, ct);

            var release = await db.ChapterReleases
                .Include(r => r.Chapter).ThenInclude(c => c.Series).ThenInclude(s => s.Tags)
                .Include(r => r.Chapter).ThenInclude(c => c.Series).ThenInclude(s => s.Authors)
                .Include(r => r.Chapter).ThenInclude(c => c.Series).ThenInclude(s => s.Artists)
                .FirstOrDefaultAsync(r => r.Id == download.ReleaseId, ct)
                ?? throw new InvalidOperationException($"Release {download.ReleaseId} not found.");

            if (release.IsExternal)
            {
                throw new InvalidOperationException("External chapters cannot be downloaded.");
            }

            var chapter = release.Chapter;
            var series = chapter.Series;
            var previousArtifactId = chapter.ActiveArtifactId;

            var source = registry.GetDownloadSource(release.SourceId);
            var pageSet = await source.GetPagesAsync(release.SourceChapterId, PageQuality.Original, ct);

            download.PagesTotal = pageSet.Pages.Count;
            await db.SaveChangesAsync(ct);

            logger.LogDebug(
                "Download {Id}: {Series} ch. {Number} — {PageCount} page(s) from {SourceId}/{ReleaseId}.",
                downloadId, series.Title, chapter.Number, pageSet.Pages.Count, release.SourceId, release.Id);

            // Not the OS temp dir — see LibraryPaths.TempRoot: it's often a small tmpfs (especially in
            // containers) that a large download's pages can exceed even with plenty of room on the
            // actual data volume.
            var tempDir = paths.NewTempDirectory($"mf-dl-{downloadId:N}");
            try
            {
                var pageFiles = await DownloadPagesAsync(pageSet, tempDir, download, ct);

                var writer = writers.Get();
                var group = release.GroupKey
                    ?? (release.ScanlationGroups.Count > 0 ? release.ScanlationGroups[0] : null);

                var request = new WriteRequest(
                    series.Title,
                    series.Authors.Select(a => a.Name).ToList(),
                    series.Tags.Where(t => t.Group == "genre").Select(t => t.Name).ToList(),
                    writer.Format,
                    paths.SeriesDirectory(series.Kind, series.Title),
                    BuildFileBaseName(series.Title, chapter, group),
                    [new ChapterSegment(
                        chapter.Number, chapter.Volume, chapter.Title, chapter.Language, group, pageFiles,
                        release.PublishedAt, BuildSourceUrl(release.SourceId, release.SourceChapterId))],
                    Artists: series.Artists.Select(a => a.Name).ToList(),
                    OtherTags: series.Tags.Where(t => t.Group != "genre").Select(t => t.Name).ToList(),
                    Description: series.Description,
                    ContentRating: series.ContentRating,
                    OriginalLanguage: series.OriginalLanguage,
                    AltTitles: series.AltTitles,
                    Kind: series.Kind);

                var result = await writer.WriteAsync(request, null, ct);

                var artifact = new Artifact
                {
                    SeriesId = series.Id,
                    Format = writer.Format,
                    Path = paths.RelativeTo(series.Kind, result.Path),
                    SizeBytes = result.SizeBytes,
                    Hash = result.Sha256,
                    Status = ArtifactStatus.Complete,
                    PageCount = result.PageCount,
                };
                artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0 });
                db.Artifacts.Add(artifact);

                chapter.ActiveArtifactId = artifact.Id;
                chapter.ActiveReleaseId = release.Id;

                download.Status = DownloadStatus.Completed;
                download.PagesDone = result.PageCount;
                download.CompletedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                // If this replaced a prior single-chapter artifact, remove it and reset page position.
                if (previousArtifactId is not null && previousArtifactId != artifact.Id)
                {
                    await ReplaceSupersededArtifactAsync(previousArtifactId.Value, chapter.Id, series.Kind, ct);
                }

                await notifier.DownloadProgressAsync(
                    download.Id, chapter.Id, DownloadStatus.Completed, download.PagesDone, download.PagesTotal, ct);
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Interrupted, not broken — a host shutdown cancels every in-flight download at once. Marking
            // these Failed would bury the user in "Download failed" admin notifications on every restart for
            // work that was merely stopped, and leave rows that read as errors but have nothing wrong with
            // them. Put it back on the queue and let Hangfire run it again.
            logger.LogInformation("Download {Id} cancelled; re-queueing.", downloadId);
            download.Status = DownloadStatus.Queued;
            download.PagesDone = 0;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Download {Id} failed.", downloadId);
            download.Status = DownloadStatus.Failed;
            download.Error = ex.Message;
            await db.SaveChangesAsync(CancellationToken.None);
            await notifier.DownloadProgressAsync(
                download.Id, download.ChapterId, DownloadStatus.Failed, download.PagesDone, download.PagesTotal, CancellationToken.None);
            await notifications.CreateForAdminsAsync(
                download.MediaKind,
                "Download failed", $"{download.Description ?? downloadId.ToString()}: {ex.Message}",
                download.SeriesId, NotificationSeverity.Error, CancellationToken.None);
            throw; // surface to Hangfire for retry
        }
    }

    private async Task<List<PageFile>> DownloadPagesAsync(
        SourcePageSet pageSet, string tempDir, Download download, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(ImageClientName);
        var pageFiles = new PageFile[pageSet.Pages.Count];
        var completed = 0;

        // Persist progress to the DB periodically (the parallel loop only touches SignalR + the local
        // array, so this task is the sole DbContext writer during the download — no concurrency issue).
        using var persistCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var persistTask = Task.Run(async () =>
        {
            try
            {
                while (!persistCts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), persistCts.Token);
                    download.PagesDone = Volatile.Read(ref completed);
                    await db.SaveChangesAsync(persistCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // stopped when the download loop finishes
            }
        });

        var options = new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentPages, CancellationToken = ct };
        try
        {
            await Parallel.ForEachAsync(pageSet.Pages, options, async (page, token) =>
        {
            var dest = Path.Combine(tempDir, $"{page.Index:D5}_{Path.GetFileName(page.FileName)}");
            var stopwatch = Stopwatch.StartNew();
            var success = false;
            var cached = false;
            long bytes = 0;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, page.Url);
                // Scraper sources supply per-image headers (Referer/User-Agent) their CDN requires;
                // page-level overrides the set-level. HttpClient only applies its default UA when the
                // request has none, so a source-supplied UA cleanly replaces the default here.
                var headers = page.Headers ?? pageSet.Headers;
                if (headers is not null)
                {
                    foreach (var (name, value) in headers)
                    {
                        request.Headers.TryAddWithoutValidation(name, value);
                    }
                }

                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                cached = response.Headers.TryGetValues("X-Cache", out var values)
                         && values.Any(v => v.Contains("HIT", StringComparison.OrdinalIgnoreCase));
                response.EnsureSuccessStatusCode();
                await using (var fs = File.Create(dest))
                {
                    await response.Content.CopyToAsync(fs, token);
                }

                bytes = new FileInfo(dest).Length;
                success = true;
            }
            finally
            {
                stopwatch.Stop();
                if (pageSet.ReportAsync is not null)
                {
                    try
                    {
                        await pageSet.ReportAsync(new PageReport(page.Url, success, cached, bytes, stopwatch.Elapsed), token);
                    }
                    catch
                    {
                        // reporting is best-effort
                    }
                }
            }

            pageFiles[page.Index] = new PageFile(page.Index, page.FileName, dest);

            var done = Interlocked.Increment(ref completed);
            await notifier.DownloadProgressAsync(
                download.Id, download.ChapterId, DownloadStatus.Running, done, pageSet.Pages.Count, token);
            });
        }
        finally
        {
            await persistCts.CancelAsync();
            try
            {
                await persistTask;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        logger.LogDebug("Download {Id}: fetched {Count} page(s) into {TempDir}.", download.Id, pageFiles.Length, tempDir);
        return [.. pageFiles];
    }

    /// <summary>Deletes a superseded artifact (file + row) when it covered only this chapter, and
    /// resets each user's page position for the chapter while keeping the read/unread flag.</summary>
    private async Task ReplaceSupersededArtifactAsync(
        Guid oldArtifactId, Guid chapterId, MediaKind kind, CancellationToken ct)
    {
        var old = await db.Artifacts
            .Include(a => a.ChapterLinks)
            .FirstOrDefaultAsync(a => a.Id == oldArtifactId, ct);

        // Only auto-remove single-chapter, downloaded artifacts; never touch a hand-imported local file
        // or a multi-chapter volume.
        if (old is null
            || old.Origin == ArtifactOrigin.Local
            || old.ChapterLinks.Count != 1
            || old.ChapterLinks[0].ChapterId != chapterId)
        {
            return;
        }

        var fullPath = paths.Absolute(kind, old.Path);
        try
        {
            if (old.Format == StorageFormat.Folder && Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
            else if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete superseded artifact file {Path}", fullPath);
        }

        db.Artifacts.Remove(old);

        var progresses = await db.ReadingProgress.Where(p => p.ChapterId == chapterId).ToListAsync(ct);
        foreach (var progress in progresses)
        {
            progress.PageIndex = 0;
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>The source's public page for a chapter, written into ComicInfo's Web field — mirrors
    /// the frontend's sourceSeriesUrl(); null for sources without a known public URL scheme.</summary>
    private static string? BuildSourceUrl(string sourceId, string sourceChapterId) => sourceId switch
    {
        "mangadex" => $"https://mangadex.org/chapter/{sourceChapterId}",
        _ => null,
    };

    private static string BuildFileBaseName(string seriesTitle, Chapter chapter, string? group)
    {
        var volume = chapter.Volume is null ? "" : $"Vol. {chapter.Volume} ";
        var number = chapter.Number is null ? "Oneshot" : $"Ch. {chapter.Number}";
        var groupTag = string.IsNullOrWhiteSpace(group) ? "" : $" [{group}]";
        return LibraryPaths.Sanitize($"{seriesTitle} - {volume}{number}{groupTag}");
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
