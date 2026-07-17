using System.Threading.RateLimiting;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.MangaDex.Auth;
using MangaFusion.Sources.MangaDex.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MangaFusion.Sources.MangaDex;

/// <summary>
/// Self-registers the MangaDex source. HttpClientFactory + resilience are authored as
/// <see cref="IServiceCollection"/> descriptors and translated into Autofac via
/// <c>builder.Populate(...)</c> — no second service provider is created. Everything else is
/// idiomatic Autofac, so the source is discovered by the registry through <see cref="ISource"/>.
/// </summary>
public class MangaDexModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var services = new ServiceCollection();

        // Auth token endpoint client — no bearer/rate-limit/resilience handlers, but MangaDex's edge
        // rejects requests without a User-Agent (400 before reaching the OAuth server), so set one.
        services.AddHttpClient(MangaDexConstants.AuthClient, c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd(MangaDexConstants.UserAgent));

        // MangaDex@Home reporting client (separate host, no auth) — still needs a User-Agent.
        services.AddHttpClient(MangaDexConstants.ReportClient, c =>
            c.DefaultRequestHeaders.UserAgent.ParseAdd(MangaDexConstants.UserAgent));

        services.AddTransient<BearerTokenHandler>();
        services.AddTransient<RateLimitingHandler>();

        // Main API client: handlers apply outer -> inner in call order, i.e.
        // resilience (retry/breaker/timeout, honours Retry-After) -> bearer (re-applied each attempt)
        // -> rate limit (throttles every network attempt against our shared 5 req/s bucket).
        var apiClient = services.AddHttpClient<MangaDexApiClient>(client =>
        {
            client.BaseAddress = new Uri(MangaDexConstants.ApiBaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MangaDexConstants.UserAgent);
        });
        apiClient.AddStandardResilienceHandler();
        apiClient.AddHttpMessageHandler<BearerTokenHandler>();
        apiClient.AddHttpMessageHandler<RateLimitingHandler>();

        builder.Populate(services);

        // Shared token bucket: 5 tokens, refilled 5/sec, with a generous queue so callers wait rather than fail.
        builder.RegisterInstance(new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,
                TokensPerPeriod = 5,
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueLimit = 1000,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true,
            }))
            .As<RateLimiter>()
            .SingleInstance();

        builder.RegisterType<MangaDexTokenProvider>().AsSelf().SingleInstance();

        builder.RegisterType<MangaDexSource>()
            .As<ISource>()
            .As<IMetadataSource>()
            .As<IChapterSource>()
            .As<IDownloadSource>()
            .As<ICredentialedSource>()
            .InstancePerLifetimeScope();
    }
}
