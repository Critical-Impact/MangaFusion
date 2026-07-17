using MangaFusion.Contracts.Models;

namespace MangaFusion.Contracts.Sources;

/// <summary>Base contract every source implements. Concrete sources additionally implement the
/// capability interfaces (<see cref="IMetadataSource"/>, <see cref="IChapterSource"/>,
/// <see cref="IDownloadSource"/>, <see cref="ICredentialedSource"/>) they support.</summary>
public interface ISource
{
    /// <summary>Stable, unique identifier, e.g. "mangadex". Used in routes and persisted links.</summary>
    string Id { get; }

    string DisplayName { get; }

    SourceCapabilities Capabilities { get; }

    /// <summary>Which libraries this source can serve. A list rather than a single value because a source
    /// isn't inherently limited to one — the manual/local ingest path already feeds both. Callers use it
    /// to offer only the sources that make sense for the library the user is currently in.</summary>
    IReadOnlyList<MediaKind> SupportedKinds { get; }
}
