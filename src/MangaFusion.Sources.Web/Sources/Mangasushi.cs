using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Mangasushi — a Madara site using the newer <c>/ajax/chapters</c> endpoint.</summary>
public sealed class Mangasushi(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "Mangasushi";
    public override string BaseUrl => "https://mangasushi.org";
    public override string Lang => "en";

    protected override bool UseNewChapterEndpoint => true;
}
