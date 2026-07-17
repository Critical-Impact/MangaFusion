using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>WitchScans — a standard MangaThemesia site.</summary>
public sealed class WitchScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "WitchScans";
    public override string BaseUrl => "https://witchscans.com";
    public override string Lang => "en";
}
