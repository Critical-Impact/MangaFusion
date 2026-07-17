using System.Threading.RateLimiting;

namespace MangaFusion.Sources.ComicVine.Http;

/// <summary>ComicVine's own rate limiter, kept as a distinct type rather than registered as the bare
/// <see cref="RateLimiter"/> service — MangaDex already claims that registration, and a second unnamed
/// one would silently win and hand MangaDex's handler the wrong (far stricter) bucket.</summary>
public sealed class ComicVineRateLimiter : IDisposable
{
    // ComicVine allows ~200 requests/hour per resource and blocks the key on sustained abuse. A small
    // burst covers an interactive search; the steady-state refill (1 per 20s = 180/hour) stays under the
    // ceiling. This does mean bulk matching a large import is slow — that's ComicVine's constraint, not
    // a tuning choice, and going faster risks the key.
    private readonly TokenBucketRateLimiter _limiter = new(new TokenBucketRateLimiterOptions
    {
        TokenLimit = 10,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromSeconds(20),
        QueueLimit = 1000,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment = true,
    });

    public ValueTask<RateLimitLease> AcquireAsync(CancellationToken ct) =>
        _limiter.AcquireAsync(permitCount: 1, ct);

    public void Dispose() => _limiter.Dispose();
}

/// <summary>Serialises outbound ComicVine calls through <see cref="ComicVineRateLimiter"/>.</summary>
public sealed class ComicVineRateLimitingHandler(ComicVineRateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(ct);
        if (!lease.IsAcquired)
        {
            throw new HttpRequestException("ComicVine rate limit queue is full; try again shortly.");
        }

        return await base.SendAsync(request, ct);
    }
}
