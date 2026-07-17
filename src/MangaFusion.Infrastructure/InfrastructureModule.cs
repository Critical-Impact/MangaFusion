using Autofac;
using MangaFusion.Application.Downloads;
using MangaFusion.Application.Library;
using MangaFusion.Application.Notifications;
using MangaFusion.Application.Reading;
using MangaFusion.Application.Settings;
using MangaFusion.Application.Tasks;
using MangaFusion.Application.Writing;
using MangaFusion.Contracts.Sources;
using MangaFusion.Infrastructure.Downloads;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Monitoring;
using MangaFusion.Infrastructure.Notifications;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Settings;
using MangaFusion.Infrastructure.Sources;
using MangaFusion.Infrastructure.Tasks;
using MangaFusion.Infrastructure.Writing;

namespace MangaFusion.Infrastructure;

/// <summary>
/// Autofac module for infrastructure services (credential storage, and later file storage, chapter
/// writers, background jobs). EF Core and Identity are registered through
/// <see cref="DependencyInjection.AddInfrastructure"/> on the <c>IServiceCollection</c> and bridged
/// into Autofac by the host.
/// </summary>
public class InfrastructureModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<DataProtectionCredentialStore>()
            .As<ISourceCredentialStore>()
            .InstancePerLifetimeScope();

        builder.RegisterInstance(TimeProvider.System).As<TimeProvider>().SingleInstance();

        builder.RegisterType<LibraryPaths>().AsSelf().SingleInstance();
        builder.RegisterType<LocalPaths>().AsSelf().SingleInstance();
        builder.RegisterType<ArtifactFileInspector>().AsSelf().SingleInstance();
        builder.RegisterType<PdfPageExtractor>().AsSelf().SingleInstance();
        builder.RegisterType<AuthorResolver>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<TagResolver>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SeriesMetadataApplier>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<SeriesCoverCache>().AsSelf().SingleInstance();
        builder.RegisterType<ChapterImporter>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ChapterFileImporter>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<LibraryService>().As<ILibraryService>().InstancePerLifetimeScope();
        builder.RegisterType<CollectionCoverComposer>().AsSelf().SingleInstance();
        builder.RegisterType<CollectionService>().As<ICollectionService>().InstancePerLifetimeScope();
        builder.RegisterType<LocalImportService>().As<ILocalLibraryService>().InstancePerLifetimeScope();

        builder.RegisterType<MigrationPaths>().AsSelf().SingleInstance();
        builder.RegisterType<MigrationScanner>().AsSelf().SingleInstance();
        builder.RegisterType<MigrationMatcher>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<MigrationCommitter>().AsSelf().InstancePerLifetimeScope();
        // AsSelf too: Hangfire's job activator resolves the concrete type (jobs.Enqueue<MigrationService>(...)).
        builder.RegisterType<MigrationService>().AsSelf().As<IMigrationService>().InstancePerLifetimeScope();

        builder.RegisterType<ImportScanner>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ImportMatcher>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ImportCommitter>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<ImportService>().AsSelf().As<IImportService>().InstancePerLifetimeScope();
        builder.RegisterType<ImportPaths>().AsSelf().InstancePerLifetimeScope();

        builder.RegisterType<WebpPageImageEncoder>().As<IPageImageEncoder>().SingleInstance();
        builder.RegisterType<PageEncodingResolver>().AsSelf().SingleInstance();
        builder.RegisterType<ArtifactPageReencoder>().AsSelf().SingleInstance();

        builder.RegisterType<CbzChapterWriter>().As<IChapterWriter>().SingleInstance();
        builder.RegisterType<FolderChapterWriter>().As<IChapterWriter>().SingleInstance();
        builder.RegisterType<ChapterWriterSelector>().AsSelf().SingleInstance();

        builder.RegisterType<CbzArtifactReader>().As<IArtifactReader>().SingleInstance();
        builder.RegisterType<FolderArtifactReader>().As<IArtifactReader>().SingleInstance();
        builder.RegisterType<ArtifactReaderRegistry>().AsSelf().SingleInstance();
        builder.RegisterType<ReaderService>().As<IReaderService>().InstancePerLifetimeScope();

        builder.RegisterType<DownloadOrchestrator>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<DownloadService>().As<IDownloadService>().InstancePerLifetimeScope();

        builder.RegisterType<NotificationService>().As<INotificationService>().InstancePerLifetimeScope();
        builder.RegisterType<SettingsService>().As<ISettingsService>().InstancePerLifetimeScope();
        builder.RegisterType<DynamicLogLevelService>().AsSelf().InstancePerLifetimeScope();
        builder.RegisterType<MonitorService>().AsSelf().InstancePerLifetimeScope();

        // The sweep owns no DbContext — it resolves a fresh scope per series (see MonitorScanJob), so it is
        // safe as a singleton and must not be scoped to one.
        builder.RegisterType<MonitorScanJob>().AsSelf().SingleInstance();

        builder.RegisterType<HangfireTaskQuery>().As<IBackgroundTaskQuery>().SingleInstance();
        builder.RegisterType<TaskFeedService>().As<ITaskFeedService>().InstancePerLifetimeScope();
    }
}
