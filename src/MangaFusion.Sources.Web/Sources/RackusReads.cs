using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Rackus — a standard MangaThemesia site.</summary>
public sealed class RackusReads(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Rackus";
    public override string BaseUrl => "https://rackusreads.com";
    public override string Lang => "en";
}
