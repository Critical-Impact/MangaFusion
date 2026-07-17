namespace MangaFusion.Contracts.Sources;

/// <summary>Stores per-source credentials as a field dictionary. The implementation encrypts values
/// at rest; secret values are never returned to clients (only used server-side by the source).
/// Lives in Contracts so sources depend only on the provider surface to read their own credentials.</summary>
public interface ISourceCredentialStore
{
    Task<IReadOnlyDictionary<string, string>?> GetAsync(string sourceId, CancellationToken ct = default);

    Task SetAsync(string sourceId, IReadOnlyDictionary<string, string> values, CancellationToken ct = default);

    Task<bool> ExistsAsync(string sourceId, CancellationToken ct = default);

    Task DeleteAsync(string sourceId, CancellationToken ct = default);
}
