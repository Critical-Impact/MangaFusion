using System.Security.Claims;
using Hangfire;
using MangaFusion.Application.Notifications;
using MangaFusion.Infrastructure.Monitoring;

namespace MangaFusion.Web.Endpoints;

public static class MonitoringEndpoints
{
    public static void MapMonitoringEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();

        // Full library scan — admin only.
        api.MapPost("/monitoring/scan", (IBackgroundJobClient jobs) =>
            {
                jobs.Enqueue<MonitorScanJob>(m => m.ScanAllAsync(CancellationToken.None));
                return Results.Accepted();
            })
            .RequireAuthorization("Admin");

        // Scan a single series on demand.
        api.MapPost("/library/series/{id:guid}/scan", (Guid id, IBackgroundJobClient jobs) =>
        {
            jobs.Enqueue<MonitorService>(m => m.ScanSeriesAsync(id, CancellationToken.None));
            return Results.Accepted();
        });

        var notifications = api.MapGroup("/notifications");

        notifications.MapGet("", async (
            ClaimsPrincipal user, INotificationService svc, string? kind, bool? unreadOnly, CancellationToken ct) =>
        {
            var userId = CurrentUser(user);
            var mediaKind = MediaKindQuery.Parse(kind);
            var items = await svc.GetForUserAsync(userId, mediaKind, unreadOnly ?? false, ct);
            var unread = await svc.UnreadCountAsync(userId, mediaKind, ct);
            return Results.Ok(new
            {
                unread,
                items = items.Select(n => new
                {
                    n.Id, n.Title, n.Body, n.SeriesId, n.CreatedAt, read = n.ReadAt != null,
                    severity = n.Severity.ToString(),
                }),
            });
        });

        notifications.MapPost("/read", async (
            ClaimsPrincipal user, MarkReadRequest request, INotificationService svc, CancellationToken ct) =>
        {
            await svc.MarkReadAsync(CurrentUser(user), request.Ids ?? [], ct);
            return Results.NoContent();
        });

        notifications.MapPost("/read-all", async (
            ClaimsPrincipal user, INotificationService svc, string? kind, CancellationToken ct) =>
        {
            // Scoped to the library the user is currently in — clearing the comic bell must not silently
            // mark every manga notification read too.
            await svc.MarkAllReadAsync(CurrentUser(user), MediaKindQuery.Parse(kind), ct);
            return Results.NoContent();
        });
    }

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}

public sealed record MarkReadRequest(Guid[]? Ids);
