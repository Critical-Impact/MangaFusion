namespace MangaFusion.Application.Sources;

/// <summary>Thrown when a source id has no registered source.</summary>
public sealed class SourceNotFoundException(string sourceId)
    : Exception($"No source registered with id '{sourceId}'.")
{
    public string SourceId { get; } = sourceId;
}

/// <summary>Thrown when a source exists but does not support the requested capability.</summary>
public sealed class SourceCapabilityException(string sourceId, string capability)
    : Exception($"Source '{sourceId}' does not support {capability}.")
{
    public string SourceId { get; } = sourceId;
    public string Capability { get; } = capability;
}
