namespace MangaFusion.Sources.MangaUpdates;

internal static class MangaUpdatesConstants
{
    public const string SourceId = "mangaupdates";
    public const string DisplayName = "MangaUpdates";

    // Trailing slash matters: HttpClient/Uri combines a relative request path onto BaseAddress per
    // RFC 3986 §5.3 — without it, the base's last path segment ("v1") would be dropped.
    public const string ApiBaseUrl = "https://api.mangaupdates.com/v1/";

    public const string UserAgent = "MangaFusion/0.1";
}
