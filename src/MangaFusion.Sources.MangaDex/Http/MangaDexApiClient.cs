using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.MangaDex.Dtos;
using MangaFusion.Sources.MangaDex.Mapping;

namespace MangaFusion.Sources.MangaDex.Http;

/// <summary>Thin typed wrapper over the MangaDex REST API. Only this class goes through
/// HttpClientFactory; the source depends on it. All resilience/auth/rate-limiting is applied to the
/// underlying <see cref="HttpClient"/> by the pipeline configured in the module.</summary>
public sealed class MangaDexApiClient(HttpClient http)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // MangaDex feed endpoints allow up to 500 per page.
    private const int MaxFeedLimit = 500;

    internal async Task<MangaListDto?> SearchMangaAsync(SearchQuery query, CancellationToken ct)
    {
        var q = new QueryBuilder();
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            q.Add("title", query.Text);
        }

        q.Add("limit", query.Limit.ToString());
        q.Add("offset", query.Offset.ToString());

        foreach (var rating in query.ContentRatings)
        {
            if (MangaDexMapper.RatingToApi(rating) is { } r)
            {
                q.Add("contentRating[]", r);
            }
        }

        foreach (var status in query.Statuses)
        {
            if (MangaDexMapper.StatusToApi(status) is { } s)
            {
                q.Add("status[]", s);
            }
        }

        foreach (var lang in query.TranslatedLanguages)
        {
            q.Add("availableTranslatedLanguage[]", lang);
        }

        foreach (var tag in query.IncludedTags)
        {
            q.Add("includedTags[]", tag);
        }

        // Verified against the live API: authors[] and artists[] combine with AND, not OR, so sending
        // the same id in both would wrongly require the person to be credited as both on a given manga.
        // The UI currently only ever links out via an "author" credit, so authors[] alone is correct.
        foreach (var authorId in query.AuthorIds)
        {
            q.Add("authors[]", authorId);
        }

        var (orderKey, orderValue) = OrderFor(query);
        q.Add(orderKey, orderValue);

        q.Add("includes[]", "cover_art");
        q.Add("includes[]", "author");
        q.Add("includes[]", "artist");

        return await GetJsonAsync<MangaListDto>($"/manga?{q}", ct);
    }

    internal async Task<MangaDataDto?> GetMangaAsync(string mangaId, CancellationToken ct)
    {
        var q = new QueryBuilder();
        q.Add("includes[]", "cover_art");
        q.Add("includes[]", "author");
        q.Add("includes[]", "artist");

        var entity = await GetJsonAsync<MangaEntityDto>($"/manga/{Uri.EscapeDataString(mangaId)}?{q}", ct);
        return entity?.Data;
    }

    internal async Task<ChapterListDto?> GetChapterFeedAsync(
        string mangaId, ChapterQuery query, CancellationToken ct)
    {
        var q = new QueryBuilder();
        q.Add("limit", Math.Clamp(query.Limit, 1, MaxFeedLimit).ToString());
        q.Add("offset", query.Offset.ToString());
        q.Add("order[chapter]", query.Order == ChapterOrder.ChapterDescending ? "desc" : "asc");
        q.Add("includes[]", "scanlation_group");

        foreach (var lang in query.TranslatedLanguages)
        {
            q.Add("translatedLanguage[]", lang);
        }

        foreach (var group in query.ScanlationGroups)
        {
            q.Add("groups[]", group);
        }

        if (query.CreatedSince is { } since)
        {
            // MangaDex expects ISO-8601 without offset/fractional seconds.
            q.Add("createdAtSince", since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss"));
        }

        // Include all ratings so a series' chapters aren't hidden by the feed's default rating filter.
        q.Add("contentRating[]", "safe");
        q.Add("contentRating[]", "suggestive");
        q.Add("contentRating[]", "erotica");
        q.Add("contentRating[]", "pornographic");

        return await GetJsonAsync<ChapterListDto>(
            $"/manga/{Uri.EscapeDataString(mangaId)}/feed?{q}", ct);
    }

    internal Task<AtHomeDto?> GetAtHomeAsync(string chapterId, CancellationToken ct) =>
        GetJsonAsync<AtHomeDto>($"/at-home/server/{Uri.EscapeDataString(chapterId)}", ct);

    internal Task<TagListDto?> GetTagsAsync(CancellationToken ct) =>
        GetJsonAsync<TagListDto>("/manga/tag", ct);

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

    private static (string Key, string Value) OrderFor(SearchQuery query) => query.Order switch
    {
        // "relevance" ordering is only meaningful with a search term.
        SearchOrder.Relevance when !string.IsNullOrWhiteSpace(query.Text) => ("order[relevance]", "desc"),
        SearchOrder.Relevance => ("order[latestUploadedChapter]", "desc"),
        SearchOrder.LatestUploadedChapter => ("order[latestUploadedChapter]", "desc"),
        SearchOrder.Title => ("order[title]", "asc"),
        SearchOrder.Year => ("order[year]", "desc"),
        SearchOrder.Rating => ("order[rating]", "desc"),
        SearchOrder.Followers => ("order[followedCount]", "desc"),
        SearchOrder.Newest => ("order[createdAt]", "desc"),
        _ => ("order[latestUploadedChapter]", "desc"),
    };

    /// <summary>Builds a query string, escaping values but leaving MangaDex's literal <c>[]</c> keys.</summary>
    private sealed class QueryBuilder
    {
        private readonly List<string> _parts = [];

        public void Add(string key, string value) =>
            _parts.Add($"{key}={Uri.EscapeDataString(value)}");

        public override string ToString() => string.Join("&", _parts);
    }
}
