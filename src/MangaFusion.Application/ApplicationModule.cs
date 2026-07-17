using Autofac;
using MangaFusion.Application.Sources;

namespace MangaFusion.Application;

/// <summary>Autofac module for application-layer services (source registry, catalog orchestration).</summary>
public class ApplicationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        // Per-lifetime-scope so the registry composes sources resolved in the same (request) scope,
        // avoiding captive dependencies on scoped source collaborators.
        builder.RegisterType<SourceRegistry>().As<ISourceRegistry>().InstancePerLifetimeScope();
        builder.RegisterType<AggregateCatalogSearch>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<CatalogService>().AsSelf().InstancePerLifetimeScope();
    }
}
