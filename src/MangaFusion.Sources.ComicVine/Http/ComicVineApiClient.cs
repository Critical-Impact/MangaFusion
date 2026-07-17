using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.ComicVine.Dtos;

namespace MangaFusion.Sources.ComicVine.Http;

/// <summary>Thrown when ComicVine reports a failure in its response envelope. Note it does this over
/// HTTP 200, so <c>EnsureSuccessStatusCode</c> alone would let these through silently.</summary>
public sealed class ComicVineApiException(int statusCode, string message)
    : Exception($"ComicVine API error {statusCode}: {message}")
{
    public int ApiStatusCode { get; } = statusCode;
}

/// <summary>Thin typed wrapper over the ComicVine REST API. Only this class goes through
/// HttpClientFactory; the source depends on it.
///
/// The API key is a query parameter on every request (there's no header/bearer form), read from the
/// credential store per-call so an admin changing it takes effect without a restart.</summary>
public sealed class ComicVineApiClient(HttpClient http, ISourceCredentialStore credentials)
{
    private const int StatusOk = 1;
    private const int StatusObjectNotFound = 101;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Request only what's mapped: the default volume payload embeds every issue plus the locations and
    // objects lists, which for a long-running title is enormous. Note the credit lists are named
    // "people"/"characters"/"concepts" here, not the *_credits names the docs list — see the remark on
    // ComicVineVolumeDto.People for why.
    private const string VolumeFields =
        "id,name,aliases,description,deck,start_year,count_of_issues,site_detail_url,image,publisher," +
        "people,characters,concepts";

    private const string IssueFields = "id,name,issue_number,cover_date,store_date";

    internal async Task<bool> HasApiKeyAsync(CancellationToken ct) =>
        await GetApiKeyOrNullAsync(ct) is not null;

    internal async Task<ComicVineResponseDto<List<ComicVineVolumeDto>>?> SearchVolumesAsync(
        SearchQuery query, CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit <= 0 ? 20 : query.Limit, 1, ComicVineConstants.MaxPageSize);

        // The /search resource returns a lighter volume than /volume/{id} does — no credit lists — which is
        // fine: a search result only needs enough to tell candidates apart (title, start year, issue count,
        // cover, site link), and the caller re-fetches the full volume once the user picks one.
        var url = await BuildUrlAsync("search", ct,
            ("resources", "volume"),
            ("query", query.Text ?? string.Empty),
            ("limit", limit.ToString(CultureInfo.InvariantCulture)),
            ("page", (query.Offset <= 0 ? 1 : query.Offset / limit + 1).ToString(CultureInfo.InvariantCulture)));

        return await GetEnvelopeAsync<List<ComicVineVolumeDto>>(url, ct);
    }

    internal async Task<ComicVineVolumeDto?> GetVolumeAsync(string volumeId, CancellationToken ct)
    {
        // Detail routes want the resource-prefixed id ("4050-12345"), while the id in a JSON body is bare.
        var url = await BuildUrlAsync(
            $"volume/{ComicVineConstants.VolumeResourcePrefix}-{Uri.EscapeDataString(volumeId)}", ct,
            ("field_list", VolumeFields));

        var envelope = await GetEnvelopeAsync<ComicVineVolumeDto>(url, ct);
        return envelope?.Results;
    }

    /// <summary>Returns the envelope, not just the items — the caller needs ComicVine's real total and the
    /// page size it actually applied in order to page correctly (see <see cref="ComicVineConstants.MaxPageSize"/>:
    /// a caller asking for 500 gets 100, and must not then advance its offset by 500).</summary>
    internal async Task<ComicVineResponseDto<List<ComicVineIssueDto>>?> GetIssuesAsync(
        string volumeId, int limit, int offset, CancellationToken ct)
    {
        var url = await BuildUrlAsync("issues", ct,
            ("filter", $"volume:{volumeId}"),
            ("field_list", IssueFields),
            ("sort", "issue_number:asc"),
            ("limit", Math.Clamp(limit, 1, ComicVineConstants.MaxPageSize).ToString(CultureInfo.InvariantCulture)),
            ("offset", Math.Max(0, offset).ToString(CultureInfo.InvariantCulture)));

        return await GetEnvelopeAsync<List<ComicVineIssueDto>>(url, ct);
    }

    private async Task<ComicVineResponseDto<T>?> GetEnvelopeAsync<T>(string url, CancellationToken ct)
        where T : class
    {
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var envelope = await response.Content.ReadFromJsonAsync<ComicVineResponseDto<T>>(JsonOptions, ct);
        if (envelope is null)
        {
            return null;
        }

        // ComicVine signals failure in the body, not the HTTP status — an invalid API key still comes
        // back as 200 OK. "Object not found" is a legitimate null rather than an error.
        if (envelope.StatusCode == StatusObjectNotFound)
        {
            return null;
        }

        if (envelope.StatusCode != StatusOk)
        {
            throw new ComicVineApiException(envelope.StatusCode, envelope.Error ?? "Unknown error");
        }

        return envelope;
    }

    private async Task<string> BuildUrlAsync(
        string path, CancellationToken ct, params (string Key, string Value)[] parameters)
    {
        var apiKey = await GetApiKeyOrNullAsync(ct)
            ?? throw new SourceNotConfiguredException(
                ComicVineConstants.SourceId,
                "No ComicVine API key is configured. Add one under Admin → Sources.");

        var query = new List<string>
        {
            $"api_key={Uri.EscapeDataString(apiKey)}",
            "format=json",
        };

        query.AddRange(parameters
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));

        return $"{path}/?{string.Join('&', query)}";
    }

    private async Task<string?> GetApiKeyOrNullAsync(CancellationToken ct)
    {
        var stored = await credentials.GetAsync(ComicVineConstants.SourceId, ct);
        return stored is not null &&
               stored.TryGetValue(ComicVineConstants.ApiKeyField, out var key) &&
               !string.IsNullOrWhiteSpace(key)
            ? key.Trim()
            : null;
    }
}
