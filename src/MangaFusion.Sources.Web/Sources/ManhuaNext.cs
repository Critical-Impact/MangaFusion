using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Manhuanext — a Madara site (new chapter endpoint).</summary>
public sealed class ManhuaNext(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "Manhuanext";
    public override string BaseUrl => "https://manhuanext.com";
    public override string Lang => "en";
    protected override bool UseNewChapterEndpoint => true;
}
