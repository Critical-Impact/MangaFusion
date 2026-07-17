using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Lagoon Scans — a standard MangaThemesia site.</summary>
public sealed class LagoonScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Lagoon Scans";
    public override string BaseUrl => "https://lagoonscans.com";
    public override string Lang => "en";
}
