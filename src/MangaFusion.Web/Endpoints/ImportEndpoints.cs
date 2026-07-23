using MangaFusion.Application.Library;

namespace MangaFusion.Web.Endpoints;

/// <summary>Admin-only metadata-assisted import wizard: scan the import inbox for release folders,
/// suggest a match for each from the batch kind's metadata source (MangaUpdates for manga, ComicVine for
/// comics), and review/commit the result.</summary>
public static class ImportEndpoints
{
    public static void MapImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/import").RequireAuthorization("Admin");

        group.MapPost("/scan", StartScan);
        group.MapGet("/batches", ListBatches);
        group.MapGet("/batches/{id:guid}", GetBatch);
        group.MapGet("/series/{id:guid}/candidates", SearchCandidates);
        group.MapPatch("/series/{id:guid}/match", SetMatch);
        group.MapPatch("/series/{id:guid}/merge-target", SetMergeTarget);
        group.MapPatch("/series/{id:guid}/title", SetTitle);
        group.MapPatch("/items/{id:guid}", SetItem);
        group.MapPost("/series/{id:guid}/commit", CommitSeries);
        group.MapPost("/series/{id:guid}/reset-stuck-commit", ResetStuckCommit);
    }

    private static async Task<IResult> StartScan(IImportService import, string? kind, CancellationToken ct)
    {
        var id = await import.StartScanAsync(MediaKindQuery.Parse(kind), ct);
        return Results.Ok(new { batchId = id });
    }

    private static async Task<IResult> ListBatches(IImportService import, CancellationToken ct) =>
        Results.Ok(await import.ListBatchesAsync(ct));

    private static async Task<IResult> GetBatch(Guid id, IImportService import, CancellationToken ct)
    {
        var batch = await import.GetBatchAsync(id, ct);
        return batch is null ? Results.NotFound() : Results.Ok(batch);
    }

    /// <summary>Ranked candidates from the batch's own metadata source. Deliberately not
    /// <c>/api/sources/{id}/search</c>: only this route knows the batch's source and how many files the
    /// series has, which is what pushes an implausibly small volume down the list.</summary>
    private static async Task<IResult> SearchCandidates(
        Guid id, string? q, IImportService import, CancellationToken ct)
    {
        try
        {
            return Results.Ok(await import.SearchCandidatesAsync(id, q ?? string.Empty, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetMatch(
        Guid id, SetMatchRequest request, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.SetSeriesMatchAsync(id, request.SourceSeriesId, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetMergeTarget(
        Guid id, SetMergeTargetRequest request, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.SetMergeTargetAsync(id, request.ExistingLibrarySeriesId, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetTitle(
        Guid id, SetTitleRequest request, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.SetTitleOverrideAsync(id, request.TitleOverride, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> SetItem(
        Guid id, SetItemRequest request, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.SetItemAsync(id, request.Include, request.Number, request.Volume, request.Title, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> CommitSeries(Guid id, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.StartCommitAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ResetStuckCommit(Guid id, IImportService import, CancellationToken ct)
    {
        try
        {
            await import.ResetStuckCommitAsync(id, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private sealed record SetMatchRequest(string? SourceSeriesId);
    private sealed record SetMergeTargetRequest(Guid? ExistingLibrarySeriesId);
    private sealed record SetTitleRequest(string? TitleOverride);
    private sealed record SetItemRequest(bool Include, string? Number, string? Volume, string? Title);
}
