using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;

namespace MangaFusion.Application.Sources;

/// <inheritdoc />
public sealed class SourceRegistry : ISourceRegistry
{
    private readonly IReadOnlyDictionary<string, ISource> _sources;

    public SourceRegistry(IEnumerable<ISource> sources)
    {
        _sources = sources
            .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ISource> All => _sources.Values.ToList();

    public IReadOnlyList<ISource> ForKind(MediaKind kind)
    {
        var contractKind = MediaKinds.ToContract(kind);
        return _sources.Values.Where(s => s.SupportedKinds.Contains(contractKind)).ToList();
    }

    public bool Contains(string id) => _sources.ContainsKey(id);

    public ISource Get(string id) =>
        _sources.TryGetValue(id, out var source) ? source : throw new SourceNotFoundException(id);

    public IMetadataSource GetMetadataSource(string id) =>
        Get(id) as IMetadataSource ?? throw new SourceCapabilityException(id, nameof(IMetadataSource));

    public IChapterSource GetChapterSource(string id) =>
        Get(id) as IChapterSource ?? throw new SourceCapabilityException(id, nameof(IChapterSource));

    public IDownloadSource GetDownloadSource(string id) =>
        Get(id) as IDownloadSource ?? throw new SourceCapabilityException(id, nameof(IDownloadSource));
}
