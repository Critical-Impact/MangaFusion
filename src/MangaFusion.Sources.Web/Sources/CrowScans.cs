using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Crow Scans — a standard MangaThemesia site.</summary>
public sealed class CrowScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Crow Scans";
    public override string BaseUrl => "https://crowscans.xyz";
    public override string Lang => "en";
}
