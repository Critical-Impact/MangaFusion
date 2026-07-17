using Hangfire;
using Hangfire.States;
using MangaFusion.Application.Tasks;

namespace MangaFusion.Web.Endpoints;

/// <summary>Admin-only background-task view: a verbose superset of the Activity page merging downloads
/// with the queue engine's scan jobs, plus retry/requeue/delete actions. A friendlier, domain-aware
/// companion to the raw /hangfire dashboard.</summary>
public static class AdminTaskEndpoints
{
    public static void MapAdminTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/tasks").RequireAuthorization("Admin");

        group.MapGet("", GetTasks);
        group.MapPost("/{downloadId:guid}/retry", RetryDownload);
        group.MapPost("/hangfire/{jobId}/requeue", RequeueJob);
        group.MapDelete("/hangfire/{jobId}", DeleteJob);
    }

    private static async Task<IResult> GetTasks(ITaskFeedService feed, int? limit, CancellationToken ct)
    {
        var take = limit is > 0 and <= 500 ? limit.Value : 100;
        return Results.Ok(await feed.GetFeedAsync(take, ct));
    }

    private static async Task<IResult> RetryDownload(Guid downloadId, ITaskFeedService feed, CancellationToken ct)
    {
        try
        {
            var id = await feed.RetryDownloadAsync(downloadId, ct);
            return Results.Ok(new { downloadId = id });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static IResult RequeueJob(string jobId, IBackgroundJobClient jobs)
    {
        var ok = jobs.ChangeState(jobId, new EnqueuedState());
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static IResult DeleteJob(string jobId, IBackgroundJobClient jobs)
    {
        var ok = jobs.ChangeState(jobId, new DeletedState());
        return ok ? Results.NoContent() : Results.NotFound();
    }
}
