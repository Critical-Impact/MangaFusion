using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>ManhwaGet — a standard Madara site.</summary>
public sealed class ManhwaGet(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "ManhwaGet";
    public override string BaseUrl => "https://manhwaget.com";
    public override string Lang => "en";
}
