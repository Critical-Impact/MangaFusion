namespace MangaFusion.Contracts.Sources;

/// <summary>Thrown by a source that needs credentials it hasn't been given — e.g. ComicVine before an
/// admin has saved an API key. Lives in Contracts (not Application) because it's a provider-surface
/// condition: a source must be able to raise it while depending only on the provider contracts.
///
/// This is a caller error, not a server fault: the web layer maps it to a 400 so the UI can prompt for
/// configuration instead of showing an opaque 500.</summary>
public sealed class SourceNotConfiguredException(string sourceId, string message)
    : Exception(message)
{
    public string SourceId { get; } = sourceId;
}
