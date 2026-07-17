using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>ManhwaNex — a Madara site using the newer <c>/ajax/chapters</c> endpoint.</summary>
public sealed class ManhwaNex(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "ManhwaNex";
    public override string BaseUrl => "https://manhwanex.com";
    public override string Lang => "en";

    protected override bool UseNewChapterEndpoint => true;
}
