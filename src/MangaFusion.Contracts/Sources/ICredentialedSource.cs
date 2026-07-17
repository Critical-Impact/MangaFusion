using MangaFusion.Contracts.Models;

namespace MangaFusion.Contracts.Sources;

/// <summary>A source that requires credentials to operate. The source reads its stored credentials
/// through the application's credential store; here it only declares the fields it needs and can
/// validate whatever is currently configured.</summary>
public interface ICredentialedSource : ISource
{
    IReadOnlyList<CredentialField> CredentialFields { get; }

    /// <summary>Attempts to authenticate with the currently stored credentials.</summary>
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);
}
