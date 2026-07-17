using MangaFusion.Sources.Web.Platforms;

namespace MangaFusion.Sources.Web.Sources;

/// <summary>Elf Toon — a standard MangaThemesia site.</summary>
public sealed class ElfToon(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
{
    public override string Name => "Elf Toon";
    public override string BaseUrl => "https://elftoon.com";
    public override string Lang => "en";
}
