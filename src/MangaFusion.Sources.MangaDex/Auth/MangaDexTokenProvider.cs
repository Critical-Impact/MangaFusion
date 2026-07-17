using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MangaFusion.Contracts.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Sources.MangaDex.Auth;

/// <summary>
/// Caches a MangaDex OAuth2 access token (15-min lifetime) and refreshes it, so callers make at most
/// one auth request per token window rather than one per API call. Singleton: it holds the cached
/// token across requests. Credentials live in a scoped, DB-backed store, so this opens a short-lived
/// scope to read them — never capturing a scoped service in this long-lived object.
/// </summary>
public sealed class MangaDexTokenProvider(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpFactory,
    ILogger<MangaDexTokenProvider> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TokenSet? _current;

    /// <summary>Returns a valid access token, or null when no credentials are configured
    /// (callers then proceed anonymously) or authentication fails.</summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_current is { } cached && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.AccessToken;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_current is { } current && current.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return current.AccessToken;
            }

            var credentials = await LoadCredentialsAsync(ct);
            if (credentials is null)
            {
                return null;
            }

            var result =
                (_current?.RefreshToken is { } refresh
                    ? await RequestTokenAsync(RefreshForm(credentials, refresh), ct)
                    : null)
                ?? await RequestTokenAsync(PasswordForm(credentials), ct);

            if (result is null)
            {
                logger.LogWarning("MangaDex authentication failed.");
                return null;
            }

            _current = result;
            return result.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forces a fresh password-grant authentication with the currently stored credentials.
    /// Used by the "test credentials" flow. Updates the cache on success.</summary>
    public async Task<bool> ValidateStoredAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var credentials = await LoadCredentialsAsync(ct);
            if (credentials is null || !HasAllFields(credentials))
            {
                return false;
            }

            var result = await RequestTokenAsync(PasswordForm(credentials), ct);
            if (result is null)
            {
                return false;
            }

            _current = result;
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> LoadCredentialsAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISourceCredentialStore>();
        var credentials = await store.GetAsync(MangaDexConstants.SourceId, ct);
        return credentials is not null && HasAllFields(credentials) ? credentials : null;
    }

    private async Task<TokenSet?> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(MangaDexConstants.AuthClient);
        using var content = new FormUrlEncodedContent(form);
        using var response = await client.PostAsync(MangaDexConstants.TokenEndpoint, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning(
                "MangaDex token request returned {Status}: {Body}", (int)response.StatusCode, body);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (token?.AccessToken is null)
        {
            return null;
        }

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn - 60));
        return new TokenSet(token.AccessToken, token.RefreshToken, expiresAt);
    }

    private static Dictionary<string, string> PasswordForm(IReadOnlyDictionary<string, string> c) => new()
    {
        ["grant_type"] = "password",
        ["username"] = c["username"],
        ["password"] = c["password"],
        ["client_id"] = c["clientId"],
        ["client_secret"] = c["clientSecret"],
    };

    private static Dictionary<string, string> RefreshForm(IReadOnlyDictionary<string, string> c, string refresh) => new()
    {
        ["grant_type"] = "refresh_token",
        ["refresh_token"] = refresh,
        ["client_id"] = c["clientId"],
        ["client_secret"] = c["clientSecret"],
    };

    private static bool HasAllFields(IReadOnlyDictionary<string, string> credentials) =>
        MangaDexConstants.CredentialKeys.All(k =>
            credentials.TryGetValue(k, out var value) && !string.IsNullOrWhiteSpace(value));

    private sealed record TokenSet(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt);

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }
}
