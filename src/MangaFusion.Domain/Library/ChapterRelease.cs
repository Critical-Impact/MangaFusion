namespace MangaFusion.Domain.Library;

/// <summary>A specific source+group variant of a logical <see cref="Chapter"/> — the unit that gets
/// downloaded. Group preference selects between the releases of a chapter.</summary>
public class ChapterRelease
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChapterId { get; set; }
    public Chapter Chapter { get; set; } = default!;

    public string SourceId { get; set; } = default!;
    public string SourceChapterId { get; set; } = default!;

    public List<string> ScanlationGroups { get; set; } = [];

    /// <summary>Normalized primary group name for matching against the series' preferred list.</summary>
    public string? GroupKey { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }
    public int? PageCount { get; set; }

    public bool IsExternal { get; set; }
    public string? ExternalUrl { get; set; }

    /// <summary>When MangaFusion first saw this release. Used by M3's grace-period timing.</summary>
    public DateTimeOffset DiscoveredAt { get; set; } = DateTimeOffset.UtcNow;
}
