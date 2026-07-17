using Autofac;
using Autofac.Extensions.DependencyInjection;
using MangaFusion.Contracts.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace MangaFusion.Sources.Web;

/// <summary>
/// Self-registers the native web-scraping sources. A single shared, resilience-wrapped
/// <c>HttpClient</c> ("web-source") is authored as <see cref="IServiceCollection"/> descriptors and
/// bridged into Autofac via <c>builder.Populate(...)</c>. Every concrete <see cref="ISource"/> in this
/// assembly is then discovered by assembly scan — adding a source is just adding a class, no wiring.
/// </summary>
public class WebSourceModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        var services = new ServiceCollection();

        services.AddHttpClient(WebSourceConstants.HttpClient, client =>
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd(WebSourceConstants.UserAgent);
                client.Timeout = TimeSpan.FromSeconds(60);
            })
            .AddStandardResilienceHandler();

        builder.Populate(services);

        // Every concrete source in this assembly (hand-written or a SourcePlatform subclass) is
        // registered against all its capability interfaces (ISource/IMetadataSource/IChapterSource/
        // IDownloadSource). The IHttpClientFactory ctor dependency resolves from the Populate above.
        builder.RegisterAssemblyTypes(typeof(WebSourceModule).Assembly)
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ISource).IsAssignableFrom(t))
            .AsImplementedInterfaces()
            // Matches the other sources' lifetime (see MangaDexModule): the SourceRegistry is
            // InstancePerLifetimeScope and is rebuilt per Hangfire job scope, so the sources it
            // enumerates must resolve in that scope too — a single root instance isn't picked up there.
            .InstancePerLifetimeScope();
    }
}
