using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>RD Scans — a Madara site (new chapter endpoint) that uses "new" as its manga URL segment.</summary>
public sealed class RDScans(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "RD Scans";
    public override string BaseUrl => "https://rdscans.com";
    public override string Lang => "en";
    protected override bool UseNewChapterEndpoint => true;
    protected override string MangaSubString => "new";
}
