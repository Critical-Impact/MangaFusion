using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Thunder Scans — a MangaThemesia site that serves series under <c>/comics</c>.</summary>
public sealed class ThunderScans(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Thunder Scans";
    public override string BaseUrl => "https://en-thunderscans.com";
    public override string Lang => "en";
    protected override string MangaUrlDirectory => "/comics";
}
