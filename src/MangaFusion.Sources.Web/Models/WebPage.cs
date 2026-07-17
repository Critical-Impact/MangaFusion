namespace MangaFusion.Sources.Web.Models;

/// <summary>A single page within a chapter — the C# port of Tachiyomi's <c>Page</c>. <see cref="Url"/>
/// is the page/referrer URL; <see cref="ImageUrl"/> is the actual image. Sources that embed the image
/// URL in the page list set <see cref="ImageUrl"/> directly; others leave it null and resolve it lazily
/// via <c>GetImageUrlAsync</c>.</summary>
public sealed class WebPage(int index, string url = "", string? imageUrl = null)
{
    public int Index { get; set; } = index;
    public string Url { get; set; } = url;
    public string? ImageUrl { get; set; } = imageUrl;
}
