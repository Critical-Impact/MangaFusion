namespace MangaFusion.Sources.ComicVine;

internal static class ComicVineConstants
{
    public const string SourceId = "comicvine";
    public const string DisplayName = "ComicVine";

    // Trailing slash matters: HttpClient/Uri combines a relative request path onto BaseAddress per
    // RFC 3986 §5.3 — without it, the base's last path segment ("api") would be dropped.
    public const string ApiBaseUrl = "https://comicvine.gamespot.com/api/";

    /// <summary>ComicVine rejects generic/absent user agents with a 403, so this must stay descriptive.</summary>
    public const string UserAgent = "MangaFusion/0.1";

    /// <summary>The single credential field: ComicVine authenticates with an API key on every request.</summary>
    public const string ApiKeyField = "apiKey";

    // ComicVine namespaces every id by resource type. The REST paths and api_detail_urls use the
    // prefixed form ("4050-12345"), while the `id` in a JSON body is the bare number — so the prefix
    // has to be added back when building a detail URL.
    public const string VolumeResourcePrefix = "4050";
    public const string IssueResourcePrefix = "4000";

    /// <summary>ComicVine's hard page size.</summary>
    public const int MaxPageSize = 100;

    /// <summary>A long-running volume can credit hundreds of characters. Past a couple of dozen they stop
    /// being useful as a filter facet and just bloat the Tag table, so the credit lists are capped.</summary>
    public const int MaxCreditsPerGroup = 25;
}
