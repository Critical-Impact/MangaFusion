namespace MangaFusion.Sources.Web.Models;

/// <summary>Mutable series description as scraped from a site — the C# port of Tachiyomi's
/// <c>SManga</c>. <see cref="Url"/> is the site path (typically without scheme/domain) that identifies
/// the series; the framework maps this onto the provider-neutral <c>SourceSeries</c>.</summary>
public sealed class WebManga
{
    /// <summary>Site path identifying the series (e.g. <c>/manga/foo/</c>). Kept domain-relative so a
    /// domain change doesn't invalidate it.</summary>
    public required string Url { get; set; }

    public string Title { get; set; } = "";
    public string? ThumbnailUrl { get; set; }
    public string? Author { get; set; }
    public string? Artist { get; set; }
    public string? Description { get; set; }

    /// <summary>Comma-separated genre string, Tachiyomi-style; use <see cref="GetGenres"/> to split.</summary>
    public string? Genre { get; set; }

    public WebMangaStatus Status { get; set; } = WebMangaStatus.Unknown;

    public IReadOnlyList<string> GetGenres() =>
        string.IsNullOrWhiteSpace(Genre)
            ? []
            : Genre.Split(',').Select(g => g.Trim()).Where(g => g.Length > 0).Distinct().ToList();
}

/// <summary>Publication status as reported by a site — mirrors Tachiyomi's <c>SManga</c> status ints.</summary>
public enum WebMangaStatus
{
    Unknown = 0,
    Ongoing,
    Completed,
    Licensed,
    PublishingFinished,
    Cancelled,
    OnHiatus,
}
