using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Sources;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MangaFusion.Web;

/// <summary>Maps source-level failures onto client errors. Without this they surface as unhandled 500s —
/// which is misleading, since each means the caller named a source that can't do what was asked (an
/// unknown source, a metadata-only source with no chapter feed, or one still missing its credentials),
/// not that the server broke.</summary>
public sealed class SourceExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            SourceNotFoundException => (StatusCodes.Status404NotFound, "Unknown source"),
            SourceCapabilityException => (StatusCodes.Status400BadRequest, "Unsupported source capability"),
            SourceNotConfiguredException => (StatusCodes.Status400BadRequest, "Source not configured"),
            _ => (0, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        context.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
            },
        });
    }
}
