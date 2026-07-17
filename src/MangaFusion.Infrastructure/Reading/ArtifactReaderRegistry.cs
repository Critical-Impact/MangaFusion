using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Picks the <see cref="IArtifactReader"/> for an artifact's <see cref="StorageFormat"/>.</summary>
public sealed class ArtifactReaderRegistry
{
    private readonly IReadOnlyDictionary<StorageFormat, IArtifactReader> _readers;

    public ArtifactReaderRegistry(IEnumerable<IArtifactReader> readers) =>
        _readers = readers.ToDictionary(r => r.Format);

    public IArtifactReader Get(StorageFormat format) => _readers[format];
}
