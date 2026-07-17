using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>RokariComics — a standard MangaThemesia site.</summary>
public sealed class RokariComics(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "RokariComics";
    public override string BaseUrl => "https://rokaricomics.com";
    public override string Lang => "en";
}
