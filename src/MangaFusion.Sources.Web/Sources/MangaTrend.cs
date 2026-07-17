using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Manga Trend — a standard MangaThemesia site.</summary>
public sealed class MangaTrend(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Manga Trend";
    public override string BaseUrl => "https://mangatrend.org";
    public override string Lang => "en";
}
