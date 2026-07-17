using System.Net;
using System.Threading.RateLimiting;

namespace MangaFusion.Sources.MangaDex.Http;

/// <summary>Throttles outgoing requests through a shared token-bucket limiter so we stay under
/// MangaDex's global ~5 req/s per IP. Placed innermost so every network attempt (including retries)
/// is throttled.</summary>
public sealed class RateLimitingHandler(RateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var lease = await limiter.AcquireAsync(1, cancellationToken);
        if (!lease.IsAcquired)
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                ReasonPhrase = "Local rate limit queue exhausted",
                RequestMessage = request,
            };
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
