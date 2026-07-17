using Hangfire;
using MangaFusion.Application.Downloads;
using MangaFusion.Application.Monitoring;
using MangaFusion.Application.Notifications;
using MangaFusion.Application.Settings;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Monitoring;

/// <summary>Scans <em>one</em> series for new releases and drives auto-downloads via the grace-period
/// planner. Runs as a Hangfire job (on-demand, per series); the full-library sweep that fans out across
/// every monitored series is <see cref="MonitorScanJob"/>, which gives each series its own DI scope so they
/// can't share — and corrupt — a DbContext.</summary>
public sealed class MonitorService(
    AppDbContext db,
    ISourceRegistry registry,
    ChapterImporter importer,
    IDownloadService downloads,
    INotificationService notifications,
    ISettingsService settings,
    SeriesMetadataApplier metadataApplier,
    TimeProvider clock,
    ILogger<MonitorService> logger)
{
    private const int FeedPageSize = 500;
    private const int MaxFeedPages = 20;

    public async Task ScanSeriesAsync(Guid seriesId, CancellationToken ct)
    {
        var series = await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Tags)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .Include(s => s.Chapters).ThenInclude(c => c.Releases)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct);
        if (series is null)
        {
            return;
        }

        var link = series.SourceLinks.FirstOrDefault(l => l.IsMetadataPrimary)
                   ?? series.SourceLinks.FirstOrDefault();

        // No fetchable source (e.g. a manually-imported local series) → nothing to scan.
        if (link is null || !registry.Contains(link.SourceId))
        {
            return;
        }

        // Refreshes title/tags/rating/etc. too, not just chapters — this is what keeps a series' Tag
        // associations (and any other drifted metadata) in sync after the initial add.
        await RefreshMetadataAsync(series, link, ct);

        // A metadata-only source (MangaUpdates, ComicVine) has no chapter feed: the metadata refresh
        // above is the whole scan. Series committed by the import wizard carry such a source as their
        // metadata-primary link, so this is the common case, not an edge case.
        if (registry.Get(link.SourceId) is not IChapterSource source)
        {
            series.LastScannedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return;
        }

        // Overlap the window slightly so nothing published right at the boundary is missed.
        var since = series.LastScannedAt?.AddHours(-6);
        var fetched = await FetchAsync(source, link.SourceSeriesId, since, ct);
        var newChapters = await importer.ImportAsync(series, fetched, ct);
        series.LastScannedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        logger.LogDebug(
            "Monitor scan: {Series} — fetched {FetchedCount} release(s) since {Since}, {NewCount} new chapter(s).",
            series.Title, fetched.Count, since?.ToString("O") ?? "(full history)", newChapters.Count);

        // Only a source that can actually serve pages is worth planning downloads for. A source that
        // lists chapters but can't download them (ComicVine's issues) contributes chapter metadata and
        // nothing else.
        if (source is IDownloadSource)
        {
            await RunAutoDownloadAsync(series, ct);
        }

        if (newChapters.Count > 0)
        {
            await NotifyFollowersAsync(series, newChapters, ct);
        }
    }

    private async Task RefreshMetadataAsync(Series series, SeriesSourceLink link, CancellationToken ct)
    {
        try
        {
            var sourceSeries = await registry.GetMetadataSource(link.SourceId).GetSeriesAsync(link.SourceSeriesId, ct);
            if (sourceSeries is not null)
            {
                await metadataApplier.ApplyAsync(series, sourceSeries, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh metadata for series {SeriesId}; chapter scan continues.", series.Id);
        }
    }

    private async Task RunAutoDownloadAsync(Series series, CancellationToken ct)
    {
        var wanted = await ResolveWantedLanguagesAsync(series, ct);
        if (wanted.Count == 0)
        {
            return;
        }

        var graceDays = series.GracePeriodDays ?? await settings.GetDefaultGraceDaysAsync(ct);
        var pending = await GetPendingChapterIdsAsync(series.Id, ct);

        var decisions = AutoDownloadPlanner.Plan(series, wanted, graceDays, clock.GetUtcNow(), pending);
        logger.LogDebug(
            "Monitor scan: {Series} — auto-download planner queued {Count} chapter(s) (grace {GraceDays}d, languages {Languages}).",
            series.Title, decisions.Count, graceDays, string.Join(',', wanted));

        foreach (var decision in decisions)
        {
            try
            {
                await downloads.QueueChapterDownloadAsync(decision.ChapterId, decision.ReleaseId, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to queue auto-download for chapter {ChapterId}.", decision.ChapterId);
            }
        }
    }

    private async Task<HashSet<string>> ResolveWantedLanguagesAsync(Series series, CancellationToken ct)
    {
        var autoFollows = await db.Follows
            .Where(f => f.SeriesId == series.Id && f.AutoDownload)
            .ToListAsync(ct);

        var autoEnabled = autoFollows.Count > 0 || series.AutoDownload;
        if (!autoEnabled)
        {
            return [];
        }

        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var follow in autoFollows)
        {
            foreach (var language in follow.Languages)
            {
                languages.Add(language);
            }
        }

        if (series.AutoDownload)
        {
            foreach (var language in series.Languages)
            {
                languages.Add(language);
            }
        }

        if (languages.Count == 0)
        {
            foreach (var language in await settings.GetDefaultLanguagesAsync(ct))
            {
                languages.Add(language);
            }
        }

        return languages;
    }

    private async Task<HashSet<Guid>> GetPendingChapterIdsAsync(Guid seriesId, CancellationToken ct)
    {
        var pending = await db.Downloads
            .Where(d => d.SeriesId == seriesId
                        && d.ChapterId != null
                        && (d.Status == DownloadStatus.Queued || d.Status == DownloadStatus.Running))
            .Select(d => d.ChapterId!.Value)
            .ToListAsync(ct);
        return pending.ToHashSet();
    }

    private async Task NotifyFollowersAsync(Series series, IReadOnlyList<Chapter> newChapters, CancellationToken ct)
    {
        var follows = await db.Follows.Where(f => f.SeriesId == series.Id).ToListAsync(ct);
        foreach (var follow in follows)
        {
            var languages = follow.Languages.Count == 0
                ? null
                : new HashSet<string>(follow.Languages, StringComparer.OrdinalIgnoreCase);

            var count = languages is null
                ? newChapters.Count
                : newChapters.Count(c => languages.Contains(c.Language));

            if (count > 0)
            {
                await notifications.CreateAsync(
                    follow.UserId, series.Kind, series.Title, $"{count} new chapter(s)", series.Id, ct: ct);
            }
        }
    }

    private static async Task<List<SourceChapter>> FetchAsync(
        IChapterSource source, string sourceSeriesId, DateTimeOffset? since, CancellationToken ct)
    {
        var all = new List<SourceChapter>();
        var offset = 0;

        for (var page = 0; page < MaxFeedPages; page++)
        {
            var result = await source.GetChaptersAsync(
                sourceSeriesId,
                new ChapterQuery { Limit = FeedPageSize, Offset = offset, IncludeExternal = true, CreatedSince = since },
                ct);

            if (result.Items.Count == 0)
            {
                break;
            }

            all.AddRange(result.Items);

            // Advance by what was actually served, not by what was asked for: a source is free to clamp the
            // page size below FeedPageSize (ComicVine caps at 100), and assuming otherwise would skip
            // straight past the issues in between.
            offset += result.Items.Count;

            if (offset >= result.Total)
            {
                break;
            }
        }

        return all;
    }
}
