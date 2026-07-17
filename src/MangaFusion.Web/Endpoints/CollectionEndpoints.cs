using System.Security.Claims;
using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Web.Models;

namespace MangaFusion.Web.Endpoints;

/// <summary>Per-user collection endpoints. Everything is scoped to the caller (no admin gate) — a user
/// only ever touches their own collections, and <see cref="ICollectionService"/> enforces that.</summary>
public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/collections").RequireAuthorization();

        group.MapGet("", ListCollections);
        group.MapPost("", CreateCollection);
        group.MapGet("/membership/{seriesId:guid}", GetMembership);
        group.MapGet("/{id:guid}", GetCollection);
        group.MapPut("/{id:guid}", UpdateCollection);
        group.MapDelete("/{id:guid}", DeleteCollection);
        group.MapPost("/{id:guid}/series/{seriesId:guid}", AddSeries);
        group.MapDelete("/{id:guid}/series/{seriesId:guid}", RemoveSeries);
        group.MapPut("/{id:guid}/order", Reorder);
        group.MapGet("/{id:guid}/cover", GetCover);
        group.MapPost("/{id:guid}/cover", UploadCover).DisableAntiforgery();
        group.MapDelete("/{id:guid}/cover", ClearCover);
    }

    private static async Task<IResult> ListCollections(
        string? kind, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var items = await collections.GetCollectionsAsync(CurrentUser(user), MediaKindQuery.Parse(kind), ct);
        return Results.Ok(items.Select(ToDto));
    }

    private static async Task<IResult> CreateCollection(
        CreateCollectionRequest request, string? kind, ClaimsPrincipal user,
        ICollectionService collections, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest("A collection name is required.");
        }

        var created = await collections.CreateAsync(
            CurrentUser(user), MediaKindQuery.Parse(kind), name, request.Description, ct);
        return Results.Ok(ToDto(created));
    }

    private static async Task<IResult> GetCollection(
        Guid id, bool? dashboard, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var detail = await collections.GetCollectionAsync(CurrentUser(user), id, dashboard ?? false, ct);
        if (detail is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new CollectionDetailDto(
            detail.Id,
            detail.Kind.ToString().ToLowerInvariant(),
            detail.Name,
            detail.Description,
            detail.MemberSort.ToString(),
            detail.DashboardFilter.ToString(),
            detail.CoverIsCustom,
            CoverUrl(detail.Id, detail.HasCover, detail.UpdatedAt),
            detail.UpdatedAt,
            detail.Members
                .Select(m => new CollectionMemberDto(m.SeriesId, m.Title, SeriesCoverUrl(m.SeriesId, m.HasCover)))
                .ToList()));
    }

    private static async Task<IResult> UpdateCollection(
        Guid id, UpdateCollectionRequest request, ClaimsPrincipal user,
        ICollectionService collections, CancellationToken ct)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Results.BadRequest("A collection name is required.");
        }

        if (!Enum.TryParse<MemberSort>(request.MemberSort, ignoreCase: true, out var sort))
        {
            return Results.BadRequest($"Unknown sort '{request.MemberSort}'.");
        }

        if (!Enum.TryParse<CollectionDashboardFilter>(request.DashboardFilter, ignoreCase: true, out var filter))
        {
            return Results.BadRequest($"Unknown dashboard filter '{request.DashboardFilter}'.");
        }

        var ok = await collections.UpdateAsync(
            CurrentUser(user), id, name, request.Description, sort, filter, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> DeleteCollection(
        Guid id, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var ok = await collections.DeleteAsync(CurrentUser(user), id, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> AddSeries(
        Guid id, Guid seriesId, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var ok = await collections.AddSeriesAsync(CurrentUser(user), id, seriesId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RemoveSeries(
        Guid id, Guid seriesId, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var ok = await collections.RemoveSeriesAsync(CurrentUser(user), id, seriesId, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> Reorder(
        Guid id, ReorderCollectionRequest request, ClaimsPrincipal user,
        ICollectionService collections, CancellationToken ct)
    {
        var ok = await collections.ReorderAsync(CurrentUser(user), id, request.SeriesIds ?? [], ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> GetMembership(
        Guid seriesId, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var ids = await collections.GetMembershipAsync(CurrentUser(user), seriesId, ct);
        return Results.Ok(ids);
    }

    private static async Task GetCover(
        Guid id, ClaimsPrincipal user, ICollectionService collections, HttpContext http, CancellationToken ct)
    {
        var file = await collections.GetCoverFileAsync(CurrentUser(user), id, ct);
        if (file is null || !File.Exists(file))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        http.Response.ContentType = "image/jpeg";
        // Short cache: covers change when membership does, and the URL's ?v= stamp busts stale copies.
        http.Response.Headers.CacheControl = "public, max-age=3600";
        await http.Response.SendFileAsync(file, ct);
    }

    private static async Task<IResult> UploadCover(
        Guid id, ClaimsPrincipal user, ICollectionService collections, HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
        {
            return Results.BadRequest("Expected a multipart form upload.");
        }

        var form = await http.Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return Results.BadRequest("No image supplied.");
        }

        await using var stream = file.OpenReadStream();
        var ok = await collections.SetCustomCoverAsync(CurrentUser(user), id, stream, file.ContentType, ct);
        return ok ? Results.NoContent() : Results.BadRequest("Collection not found or the image was invalid.");
    }

    private static async Task<IResult> ClearCover(
        Guid id, ClaimsPrincipal user, ICollectionService collections, CancellationToken ct)
    {
        var ok = await collections.ClearCustomCoverAsync(CurrentUser(user), id, ct);
        return ok ? Results.NoContent() : Results.NotFound();
    }

    private static CollectionDto ToDto(CollectionSummary c) => new(
        c.Id,
        c.Kind.ToString().ToLowerInvariant(),
        c.Name,
        c.Description,
        c.MemberSort.ToString(),
        c.DashboardFilter.ToString(),
        CoverUrl(c.Id, c.HasCover, c.UpdatedAt),
        c.ItemCount,
        c.UpdatedAt);

    private static string? CoverUrl(Guid id, bool hasCover, DateTimeOffset updatedAt) =>
        hasCover ? $"/api/collections/{id}/cover?v={updatedAt.UtcTicks}" : null;

    private static string? SeriesCoverUrl(Guid seriesId, bool hasCover) =>
        hasCover ? $"/api/library/series/{seriesId}/cover" : null;

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
