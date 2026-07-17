using Autofac;
using Autofac.Extensions.DependencyInjection;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.MangaUpdates.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MangaFusion.Sources.MangaUpdates;

/// <summary>
/// Self-registers the MangaUpdates source. HttpClientFactory + resilience are authored as
/// <see cref="IServiceCollection"/> descriptors and translated into Autofac via
/// <c>builder.Populate(...)</c>, mirroring <c>MangaDexModule</c>. No auth is required — MangaUpdates'
/// search/get endpoints are public — so there's no token provider or bearer handler here.
/// </summary>
public class MangaUpdatesModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var services = new ServiceCollection();

        var apiClient = services.AddHttpClient<MangaUpdatesApiClient>(client =>
        {
            client.BaseAddress = new Uri(MangaUpdatesConstants.ApiBaseUrl);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(MangaUpdatesConstants.UserAgent);
        });
        apiClient.AddStandardResilienceHandler();

        builder.Populate(services);

        builder.RegisterType<MangaUpdatesSource>()
            .As<ISource>()
            .As<IMetadataSource>()
            .InstancePerLifetimeScope();
    }
}
