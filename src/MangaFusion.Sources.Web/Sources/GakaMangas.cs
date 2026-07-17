using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>GakaMangas — a Madara site (new chapter endpoint).</summary>
public sealed class GakaMangas(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "GakaMangas";
    public override string BaseUrl => "https://gakamangas.com";
    public override string Lang => "en";
    protected override bool UseNewChapterEndpoint => true;
}
