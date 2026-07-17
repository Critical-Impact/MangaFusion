namespace MangaFusion.Sources.Web.Models;

/// <summary>Mutable chapter description as scraped from a site — the C# port of Tachiyomi's
/// <c>SChapter</c>.</summary>
public sealed class WebChapter
{
    /// <summary>Site path identifying the chapter (kept domain-relative).</summary>
    public required string Url { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Recognised chapter number, or <c>-1</c> when unknown. Usually derived from
    /// <see cref="Name"/> via <see cref="Util.ChapterRecognition"/>.</summary>
    public float ChapterNumber { get; set; } = -1f;

    public string? Scanlator { get; set; }
    public DateTimeOffset? DateUpload { get; set; }
}
