using Hangfire;
using MangaFusion.Application.Notifications;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Monitoring;

/// <summary>The full-library sweep: scans every monitored series, one at a time.
///
/// Deliberately a separate type from <see cref="MonitorService"/>, holding <em>no</em> DbContext of its own.
/// Hangfire activates a job in a single DI scope, so a sweep that ran as a method on MonitorService shared
/// one <see cref="AppDbContext"/> across every series it touched: the change tracker accumulated the full
/// entity graph of the whole library for the duration of the run, and — worse — a series that threw
/// half-way through having its metadata applied left those dirty entities tracked, so the *next* series'
/// SaveChanges silently committed them. Owning no context means this class cannot reintroduce that; each
/// series gets a fresh scope, and a failed one's context is discarded with it.</summary>
public sealed class MonitorScanJob(IServiceScopeFactory scopes, ILogger<MonitorScanJob> logger)
{
    [AutomaticRetry(Attempts = 0)]
    public async Task ScanAllAsync(CancellationToken ct)
    {
        var monitored = await GetMonitoredSeriesIdsAsync(ct);
        logger.LogInformation("Monitoring scan: {Count} series.", monitored.Count);

        var scanned = 0;
        foreach (var id in monitored)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<MonitorService>().ScanSeriesAsync(id, ct);
                scanned++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Shutdown — abandon the rest of the sweep rather than reporting every remaining series as a
                // failure. The next scheduled run picks up where this one left off.
                logger.LogInformation("Monitoring scan cancelled after {Count} series.", scanned);
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Scan failed for series {SeriesId}.", id);
                await NotifyFailureAsync(id, ex);
            }
        }
    }

    private async Task<IReadOnlyList<Guid>> GetMonitoredSeriesIdsAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var followed = db.Follows.Select(f => f.SeriesId);
        var auto = db.Series.Where(s => s.AutoDownload).Select(s => s.Id);
        return await followed.Union(auto).Distinct().ToListAsync(ct);
    }

    /// <summary>Reports a failed series scan to the admins from a <em>fresh</em> scope — the scope the scan
    /// failed in still tracks whatever it had half-applied, and writing the notification through that
    /// context would flush those changes as a side effect of reporting the error.</summary>
    private async Task NotifyFailureAsync(Guid seriesId, Exception ex)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var series = await db.Series
                .Where(s => s.Id == seriesId)
                .Select(s => new { s.Title, s.Kind })
                .FirstOrDefaultAsync(CancellationToken.None);

            await notifications.CreateForAdminsAsync(
                series?.Kind ?? MediaKind.Manga,
                "Series scan failed",
                $"{series?.Title ?? "Unknown series"}: {ex.Message}",
                seriesId,
                NotificationSeverity.Warning,
                CancellationToken.None);
        }
        catch (Exception notifyEx)
        {
            // Never let reporting a scan failure abort the sweep over the series that still haven't run.
            logger.LogWarning(notifyEx, "Failed to report the scan failure for series {SeriesId}.", seriesId);
        }
    }
}
