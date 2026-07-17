using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>MangaDia — a Madara site using the newer <c>/ajax/chapters</c> endpoint.</summary>
public sealed class MangaDia(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "MangaDia";
    public override string BaseUrl => "https://mangadia.com";
    public override string Lang => "en";

    protected override bool UseNewChapterEndpoint => true;
}
