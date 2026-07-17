using Autofac;
using Autofac.Extensions.DependencyInjection;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.ComicVine.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MangaFusion.Sources.ComicVine;

/// <summary>
/// Self-registers the ComicVine source. HttpClientFactory + resilience are authored as
/// <see cref="IServiceCollection"/> descriptors and translated into Autofac via
/// <c>builder.Populate(...)</c>, mirroring <c>MangaDexModule</c>.
///
/// Auth is a single API key sent as a query parameter, so there's no token provider or bearer handler —
/// <see cref="ComicVineApiClient"/> reads the key straight from the credential store per request.
/// </summary>
public class ComicVineModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<ComicVineRateLimiter>().AsSelf().SingleInstance();
        builder.RegisterType<ComicVineRateLimitingHandler>().AsSelf().InstancePerDependency();

        var services = new ServiceCollection();

        var apiClient = services.AddHttpClient<ComicVineApiClient>(client =>
        {
            client.BaseAddress = new Uri(ComicVineConstants.ApiBaseUrl);

            // ComicVine 403s requests with a generic or absent User-Agent.
            client.DefaultRequestHeaders.UserAgent.ParseAdd(ComicVineConstants.UserAgent);
        });

        apiClient.AddStandardResilienceHandler();
        apiClient.AddHttpMessageHandler(sp => sp.GetRequiredService<ComicVineRateLimitingHandler>());

        builder.Populate(services);

        builder.RegisterType<ComicVineSource>()
            .As<ISource>()
            .As<IMetadataSource>()
            .As<IChapterSource>()
            .As<ICredentialedSource>()
            .InstancePerLifetimeScope();
    }
}
