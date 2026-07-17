using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Manhua Plus — a Madara site (old chapter endpoint) that serves chapter pages under
/// <c>.read-container</c> rather than the default <c>.reading-content</c>.</summary>
public sealed class ManhuaPlus(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
{
    public override string Name => "Manhua Plus";
    public override string BaseUrl => "https://manhuaplus.com";
    public override string Lang => "en";

    protected override string PageListSelector => ".read-container img";
}
