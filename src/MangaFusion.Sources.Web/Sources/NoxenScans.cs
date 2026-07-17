using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Noxen Scans — a standard MangaThemesia site.</summary>
public sealed class NoxenScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Noxen Scans";
    public override string BaseUrl => "https://noxenscan.com";
    public override string Lang => "en";
}
