using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Picks the <see cref="IArtifactReader"/> for an artifact's <see cref="StorageFormat"/>.</summary>
public sealed class ArtifactReaderRegistry
{
    private readonly IReadOnlyDictionary<StorageFormat, IArtifactReader> _readers;

    public ArtifactReaderRegistry(IEnumerable<IArtifactReader> readers) =>
        _readers = readers.ToDictionary(r => r.Format);

    // StorageFormat.Prose is intentionally absent: prose is read through the parallel IProseArtifactReader,
    // not this registry, so a Prose lookup throwing KeyNotFoundException is a canary that a light novel was
    // routed down the image-page path — not a gap to fill with a branch.
    public IArtifactReader Get(StorageFormat format) => _readers[format];
}
