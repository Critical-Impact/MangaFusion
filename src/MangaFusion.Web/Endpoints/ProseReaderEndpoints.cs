using System.Security.Claims;
using MangaFusion.Application.Reading;
using MangaFusion.Web.Models;

namespace MangaFusion.Web.Endpoints;

/// <summary>In-app prose reader: chapter manifest, sanitized chapter HTML, inline image bytes (cached),
/// and per-user scroll progress for light novels. Sibling to <see cref="ReaderEndpoints"/> (which stays
/// untouched) on the same <c>/api/library</c> group; chapter navigation and Continue-reading are shared
/// with it. Shared library, so any authenticated user may read; progress is per-user via the auth
/// cookie.</summary>
public static class ProseReaderEndpoints
{
    public static void MapProseReaderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/library").RequireAuthorization();

        group.MapGet("/chapters/{id:guid}/prose/manifest", GetManifest);
        group.MapGet("/chapters/{id:guid}/prose/content", GetContent);
        group.MapGet("/chapters/{id:guid}/prose/images/{name}", GetImage);
        group.MapPut("/chapters/{id:guid}/prose/progress", SaveProgress);
        group.MapGet("/chapters/{id:guid}/pdf", GetPdf);
        group.MapGet("/chapters/{id:guid}/pdf/manifest", GetPdfManifest);
        group.MapPut("/chapters/{id:guid}/pdf/progress", SavePdfProgress);
    }

    private static async Task<IResult> GetPdfManifest(
        Guid id, ClaimsPrincipal user, IProseReaderService reader, CancellationToken ct)
    {
        var manifest = await reader.GetPdfManifestAsync(CurrentUser(user), id, ct);
        return manifest is null ? Results.NotFound() : Results.Ok(manifest);
    }

    private static async Task<IResult> SavePdfProgress(
        Guid id, SavePdfProgressRequest request, ClaimsPrincipal user, IProseReaderService reader,
        CancellationToken ct)
    {
        await reader.SavePdfProgressAsync(CurrentUser(user), id, request.Page, request.Completed, ct);
        return Results.NoContent();
    }

    /// <summary>Streams a light-novel chapter's stored-as-is PDF for the PDF.js reader, with range +
    /// ETag/304 support (<see cref="Results.File(string,string?,string?,DateTimeOffset?,EntityTagHeaderValue,bool)"/>
    /// handles conditional/partial requests, which lets PDF.js load pages progressively).</summary>
    private static async Task<IResult> GetPdf(Guid id, IProseReaderService reader, CancellationToken ct)
    {
        var pdf = await reader.ResolvePdfAsync(id, ct);
        if (pdf is null || !File.Exists(pdf.AbsolutePath))
        {
            return Results.NotFound();
        }

        return Results.File(
            pdf.AbsolutePath, "application/pdf", enableRangeProcessing: true,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue(pdf.ETag));
    }

    private static Guid CurrentUser(ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static async Task<IResult> GetManifest(
        Guid id, ClaimsPrincipal user, IProseReaderService reader, CancellationToken ct)
    {
        var manifest = await reader.GetProseManifestAsync(CurrentUser(user), id, ct);
        return manifest is null ? Results.NotFound() : Results.Ok(manifest);
    }

    private static async Task<IResult> GetContent(Guid id, IProseReaderService reader, CancellationToken ct)
    {
        var content = await reader.GetProseContentAsync(id, ct);
        return content is null ? Results.NotFound() : Results.Ok(content);
    }

    private static async Task<IResult> GetImage(
        Guid id, string name, IProseReaderService reader, HttpContext http, CancellationToken ct)
    {
        var ifNoneMatch = http.Request.Headers.IfNoneMatch.ToString();
        var image = await reader.OpenProseImageAsync(id, name, ifNoneMatch, ct);
        if (image is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = image.ETag;
        if (image.NotModified)
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        http.Response.Headers.CacheControl = "private, max-age=86400, immutable";
        return Results.Stream(image.Stream!, image.ContentType!);
    }

    private static async Task<IResult> SaveProgress(
        Guid id, SaveProseProgressRequest request, ClaimsPrincipal user, IProseReaderService reader,
        CancellationToken ct)
    {
        await reader.SaveProseProgressAsync(CurrentUser(user), id, request.ScrollFraction, request.Completed, ct);
        return Results.NoContent();
    }
}
