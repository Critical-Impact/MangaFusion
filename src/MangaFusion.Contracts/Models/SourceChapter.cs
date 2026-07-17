namespace MangaFusion.Contracts.Models;

/// <summary>A chapter as described by a source. <see cref="Number"/> is a string on purpose:
/// chapters can be "10.5", decimals, or absent (oneshots).</summary>
public sealed record SourceChapter
{
    public required string SourceId { get; init; }
    public required string SourceChapterId { get; init; }
    public string? Volume { get; init; }
    public string? Number { get; init; }
    public string? Title { get; init; }
    public required string Language { get; init; }
    public IReadOnlyList<string> ScanlationGroups { get; init; } = [];
    public int? PageCount { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>True when the chapter is hosted off-site (not downloadable via this source).</summary>
    public bool IsExternal { get; init; }
    public string? ExternalUrl { get; init; }
}
