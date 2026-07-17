using MangaFusion.Application.Library;

namespace MangaFusion.Web.Endpoints;

/// <summary>Admin-only local/manual library: create hand-curated series and import existing CBZ/folder
/// files (from the configured inbox) as their chapters. Imported chapters are read through the normal
/// reader.</summary>
public static class LocalEndpoints
{
    public static void MapLocalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/local").RequireAuthorization("Admin");

        group.MapGet("/series", ListSeries);
        group.MapPost("/series", CreateSeries);
        group.MapPut("/series/{id:guid}", UpdateSeries);
        group.MapGet("/inbox", ListInbox);
        group.MapPost("/series/{id:guid}/import", Import);
    }

    /// <summary>The kind comes from <c>?kind=</c>, not the body — the SPA sends the library the admin is
    /// currently in. Without it every locally-created series silently fell back to the enum's default and
    /// became a Manga, even while the page's own tag picker was showing comic tags.</summary>
    private static async Task<IResult> CreateSeries(
        LocalSeriesMetadata metadata, string? kind, ILocalLibraryService local, CancellationToken ct)
    {
        try
        {
            var id = await local.CreateSeriesAsync(metadata with { Kind = MediaKindQuery.Parse(kind) }, ct);
            return Results.Ok(new { id });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> UpdateSeries(
        Guid id, LocalSeriesMetadata metadata, ILocalLibraryService local, CancellationToken ct)
    {
        try
        {
            await local.UpdateSeriesAsync(id, metadata, ct);
            return Results.NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static async Task<IResult> ListSeries(
        string? kind, ILocalLibraryService local, CancellationToken ct) =>
        Results.Ok(await local.ListSeriesAsync(MediaKindQuery.Parse(kind), ct));

    private static async Task<IResult> ListInbox(
        string? kind, ILocalLibraryService local, CancellationToken ct) =>
        Results.Ok(await local.ListInboxAsync(MediaKindQuery.Parse(kind), ct));

    private static async Task<IResult> Import(
        Guid id, LocalImportRequest request, ILocalLibraryService local, CancellationToken ct)
    {
        try
        {
            var imported = await local.ImportAsync(id, request, ct);
            return Results.Ok(new { imported });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
