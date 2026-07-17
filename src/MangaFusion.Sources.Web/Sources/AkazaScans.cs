using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Akaza Scans — a standard MangaThemesia site.</summary>
public sealed class AkazaScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Akaza Scans";
    public override string BaseUrl => "https://akazascans.org";
    public override string Lang => "en";
}
