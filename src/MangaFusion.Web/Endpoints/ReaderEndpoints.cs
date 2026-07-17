using System.Security.Claims;
using MangaFusion.Application.Reading;
using MangaFusion.Web.Models;

namespace MangaFusion.Web.Endpoints;

/// <summary>In-app reader: chapter manifest, page bytes (cached), per-user progress, chapter
/// navigation, and the "Continue reading" feed. Shared library, so any authenticated user may read
/// any downloaded chapter; progress is per-user via the auth cookie.</summary>
public static class ReaderEndpoints
{
    public static void MapReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library").RequireAuthorization();

        group.MapGet("/chapters/{id:guid}/manifest", GetManifest);
        group.MapGet("/chapters/{id:guid}/pages/{index:int}", GetPage);
        group.MapPut("/chapters/{id:guid}/progress", SaveProgress);
        group.MapGet("/chapters/{id:guid}/neighbors", GetNeighbors);
        group.MapGet("/continue-reading", ContinueReading);
        group.MapPost("/series/{id:guid}/reading", AddReading);
        group.MapDelete("/series/{id:guid}/reading", DismissReading);
    }

    private static async Task<IResult> AddReading(
        Guid id, ClaimsPrincipal user, IReaderService reader, CancellationToken ct)
    {
        await reader.SetReadingAsync(CurrentUser(user), id, dismissed: false, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DismissReading(
        Guid id, ClaimsPrincipal user, IReaderService reader, CancellationToken ct)
    {
        await reader.SetReadingAsync(CurrentUser(user), id, dismissed: true, ct);
        return Results.NoContent();
    }

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> GetManifest(
        Guid id, ClaimsPrincipal user, IReaderService reader, CancellationToken ct)
    {
        var manifest = await reader.GetManifestAsync(CurrentUser(user), id, ct);
        return manifest is null ? Results.NotFound() : Results.Ok(manifest);
    }

    private static async Task<IResult> GetPage(
        Guid id, int index, IReaderService reader, HttpContext http, CancellationToken ct)
    {
        var ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();
        var page = await reader.OpenPageAsync(id, index, ifNoneMatch, ct);
        if (page is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = page.ETag;
        if (page.NotModified)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return Results.Stream(page.Stream!, page.ContentType!);
    }

    private static async Task<IResult> SaveProgress(
        Guid id, SaveProgressRequest request, ClaimsPrincipal user, IReaderService reader, CancellationToken ct)
    {
        await reader.SaveProgressAsync(CurrentUser(user), id, request.PageIndex, request.Completed, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetNeighbors(Guid id, IReaderService reader, CancellationToken ct) =>
        Results.Ok(await reader.GetNeighborsAsync(id, ct));

    /// <summary>An absent <c>?kind=</c> means both libraries — the client omits it when the user has
    /// opted into a combined Home (<c>ApplicationUser.HomeAcrossLibraries</c>).</summary>
    private static async Task<IResult> ContinueReading(
        ClaimsPrincipal user, IReaderService reader, string? kind, int? limit, CancellationToken ct)
    {
        var take = limit is > 0 and <= 50 ? limit.Value : 12;
        var items = await reader.GetContinueReadingAsync(
            CurrentUser(user), MediaKindQuery.ParseOptional(kind), take, ct);
        return Results.Ok(items.Select(i => new
        {
            i.SeriesId,
            i.SeriesTitle,
            coverUrl = i.CoverPath is null ? null : $"/api/library/series/{i.SeriesId}/cover",
            i.ChapterId,
            i.Number,
            i.Volume,
            i.Language,
            i.PageIndex,
            i.PageCount,
            i.UpdatedAt,
        }));
    }
}
