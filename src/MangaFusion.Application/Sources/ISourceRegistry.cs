using MangaFusion.Contracts.Sources;

namespace MangaFusion.Application.Sources;

/// <summary>Resolves registered sources (and their capabilities) by id. Sources self-register via
/// their assembly's Autofac module; this registry is the single lookup point for the rest of the app.</summary>
public interface ISourceRegistry
{
    IReadOnlyList<ISource> All { get; }

    /// <summary>The sources that serve a given library — what a source picker should offer the user
    /// while they're in manga mode vs comic mode.</summary>
    IReadOnlyList<ISource> ForKind(MediaKind kind);

    bool Contains(string id);

    /// <summary>Gets a source or throws <see cref="SourceNotFoundException"/>.</summary>
    ISource Get(string id);

    /// <summary>Gets a source as a metadata source, or throws <see cref="SourceCapabilityException"/>.</summary>
    IMetadataSource GetMetadataSource(string id);

    IChapterSource GetChapterSource(string id);

    IDownloadSource GetDownloadSource(string id);
}
