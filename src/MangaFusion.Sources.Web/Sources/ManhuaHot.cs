using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>ManhuaHot — a standard Madara site (default configuration).</summary>
public sealed class ManhuaHot(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "ManhuaHot";
    public override string BaseUrl => "https://manhuahot.com";
    public override string Lang => "en";
}
