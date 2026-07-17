using MangaFusion.Application.Library;

namespace MangaFusion.Web.Endpoints;

/// <summary>Admin-only CBZ migration tool: scan the inbox for an old downloader's series folders,
/// auto-match them against MangaDex, and review/commit the result.</summary>
public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/migration").RequireAuthorization("Admin");

        group.MapPost("/scan", StartScan);
        group.MapGet("/batches", ListBatches);
        group.MapGet("/batches/{id:guid}", GetBatch);
        group.MapPatch("/series/{id:guid}/match", SetMatch);
        group.MapPatch("/series/{id:guid}/merge-target", SetMergeTarget);
        group.MapPatch("/items/{id:guid}", SetItemDisposition);
        group.MapPost("/series/{id:guid}/commit", CommitSeries);
        group.MapPost("/batches/{id:guid}/commit-clean", CommitAllClean);
    }

    private static async Task<IResult> StartScan(IMigrationService migration, CancellationToken ct)
    {
        var id = await migration.StartScanAsync(ct);
        return Results.Ok(new { batchId = id });
    }

    private static async Task<IResult> ListBatches(IMigrationService migration, CancellationToken ct) =>
        Results.Ok(await migration.ListBatchesAsync(ct));

    private static async Task<IResult> GetBatch(Guid id, IMigrationService migration, CancellationToken ct)
    {
        var batch = await migration.GetBatchAsync(id, ct);
        return batch is null ? Results.NotFound() : Results.Ok(batch);
    }

    private static async Task<IResult> SetMatch(
        Guid id, SetMatchRequest request, IMigrationService migration, CancellationToken ct)
    {
        try
        {
            await migration.RematchSeriesAsync(id, request.SourceSeriesId, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetMergeTarget(
        Guid id, SetMergeTargetRequest request, IMigrationService migration, CancellationToken ct)
    {
        try
        {
            await migration.SetMergeTargetAsync(id, request.ExistingLibrarySeriesId, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetItemDisposition(
        Guid id, SetItemDispositionRequest request, IMigrationService migration, CancellationToken ct)
    {
        try
        {
            await migration.SetItemDispositionAsync(id, request.Disposition, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CommitSeries(Guid id, IMigrationService migration, CancellationToken ct)
    {
        try
        {
            await migration.CommitSeriesAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CommitAllClean(Guid id, IMigrationService migration, CancellationToken ct)
    {
        var committed = await migration.CommitAllCleanAsync(id, ct);
        return Results.Ok(new { committed });
    }

    private sealed record SetMatchRequest(string? SourceSeriesId);
    private sealed record SetMergeTargetRequest(Guid? ExistingLibrarySeriesId);
    private sealed record SetItemDispositionRequest(string Disposition);
}
