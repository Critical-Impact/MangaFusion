using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>TritiniaScans — a standard Madara site (default configuration).</summary>
public sealed class TritiniaScans(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "TritiniaScans";
    public override string BaseUrl => "https://tritinia.org";
    public override string Lang => "en";
}
