using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.MangaUpdates.Dtos;

namespace MangaFusion.Sources.MangaUpdates.Http;

/// <summary>Thin typed wrapper over the public (unauthenticated) MangaUpdates REST API. Only this
/// class goes through HttpClientFactory; the source depends on it.</summary>
public sealed class MangaUpdatesApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    internal async Task<SeriesSearchResponseDto?> SearchSeriesAsync(SearchQuery query, CancellationToken ct)
    {
        var perPage = Math.Clamp(query.Limit <= 0 ? 20 : query.Limit, 1, 100);
        var body = new
        {
            search = query.Text,
            stype = "title",
            perpage = perPage,
            page = query.Offset <= 0 ? 1 : query.Offset / perPage + 1,
        };

        // No leading "/" — MangaUpdatesConstants.ApiBaseUrl includes the "/v1" path segment, and a
        // leading "/" here would make Uri treat this as an absolute-path reference, silently
        // dropping "/v1" from the combined URL (verified live: that 404/405s).
        using var response = await http.PostAsJsonAsync("series/search", body, JsonOptions, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SeriesSearchResponseDto>(JsonOptions, ct);
    }

    internal Task<SeriesModelDto?> GetSeriesAsync(string seriesId, CancellationToken ct) =>
        GetJsonAsync<SeriesModelDto>($"series/{Uri.EscapeDataString(seriesId)}", ct);

    internal async Task<IReadOnlyList<GenreStatsDto>> GetGenresAsync(CancellationToken ct) =>
        await GetJsonAsync<List<GenreStatsDto>>("genres", ct) ?? [];

    private async Task<T?> GetJsonAsync<T>(string relativeUrl, CancellationToken ct) where T : class
    {
        using var response = await http.GetAsync(relativeUrl, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
    }
}
