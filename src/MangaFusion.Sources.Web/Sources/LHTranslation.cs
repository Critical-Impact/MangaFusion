using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>LHTranslation — a Madara site (new chapter endpoint).</summary>
public sealed class LHTranslation(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "LHTranslation";
    public override string BaseUrl => "https://lhtranslation.net";
    public override string Lang => "en";
    protected override bool UseNewChapterEndpoint => true;
}
