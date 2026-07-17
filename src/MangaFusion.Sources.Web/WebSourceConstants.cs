namespace MangaFusion.Sources.Web;

/// <summary>Shared constants for the native web-scraping source framework.</summary>
public static class WebSourceConstants
{
    /// <summary>Name of the shared, resilience-wrapped <see cref="System.Net.Http.HttpClient"/> every
    /// web source pulls from <see cref="System.Net.Http.IHttpClientFactory"/>.</summary>
    public const string HttpClient = "web-source";

    /// <summary>A desktop browser User-Agent. Many scraper targets reject non-browser agents, so we
    /// present a realistic one (mirrors what Tachiyomi/Mihon ship as their default UA).</summary>
    public const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "Chrome/128.0.0.0 Safari/537.36";
}
