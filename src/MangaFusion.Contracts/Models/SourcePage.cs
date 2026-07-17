namespace MangaFusion.Contracts.Models;

/// <summary>A single resolvable page image within a chapter.</summary>
public sealed record SourcePage(int Index, string Url, string FileName)
{
    /// <summary>Optional per-image request headers (e.g. <c>Referer</c>) the downloader must apply
    /// when fetching this image. Falls back to <see cref="SourcePageSet.Headers"/> when null. Scraper
    /// sources set this because many CDNs reject image requests lacking a matching Referer/User-Agent;
    /// API sources like MangaDex leave it null.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>The resolved, ordered set of page images for a chapter, plus an optional callback the
/// downloader (M2) invokes to report per-image success/failure back to the source's delivery
/// network (e.g. MangaDex@Home reporting).</summary>
public sealed record SourcePageSet
{
    public required string SourceChapterId { get; init; }
    public required IReadOnlyList<SourcePage> Pages { get; init; }
    public PageQuality Quality { get; init; } = PageQuality.Original;
    public Func<PageReport, CancellationToken, Task>? ReportAsync { get; init; }

    /// <summary>Optional request headers the downloader applies to every page image that doesn't
    /// carry its own <see cref="SourcePage.Headers"/>. Null for sources whose image CDN needs no
    /// special headers.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }
}

/// <summary>Outcome of downloading one page image, for delivery-network reporting.</summary>
public sealed record PageReport(string Url, bool Success, bool Cached, long Bytes, TimeSpan Duration);
