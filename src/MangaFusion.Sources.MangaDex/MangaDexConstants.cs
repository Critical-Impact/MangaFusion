namespace MangaFusion.Sources.MangaDex;

internal static class MangaDexConstants
{
    public const string SourceId = "mangadex";
    public const string DisplayName = "MangaDex";

    public const string ApiBaseUrl = "https://api.mangadex.org";
    public const string UploadsBaseUrl = "https://uploads.mangadex.org";
    public const string TokenEndpoint =
        "https://auth.mangadex.org/realms/mangadex/protocol/openid-connect/token";

    /// <summary>Named client used only for the auth token endpoint (no bearer/rate-limit handlers).</summary>
    public const string AuthClient = "mangadex-auth";

    /// <summary>Named client for MangaDex@Home success/failure reporting (different host, no auth).</summary>
    public const string ReportClient = "mangadex-report";
    public const string ReportEndpoint = "https://api.mangadex.network/report";

    public const string UserAgent = "MangaFusion/0.1";

    public static readonly string[] CredentialKeys = ["clientId", "clientSecret", "username", "password"];
}
