namespace MangaFusion.Contracts.Models;

/// <summary>Criteria for searching a source's catalogue of series.</summary>
public sealed record SearchQuery
{
    public string? Text { get; init; }
    public int Limit { get; init; } = 20;
    public int Offset { get; init; }
    public IReadOnlyList<ContentRating> ContentRatings { get; init; } = [];
    public IReadOnlyList<PublicationStatus> Statuses { get; init; } = [];
    public IReadOnlyList<string> TranslatedLanguages { get; init; } = [];
    public IReadOnlyList<string> IncludedTags { get; init; } = [];
    public IReadOnlyList<string> AuthorIds { get; init; } = [];
    public SearchOrder Order { get; init; } = SearchOrder.Relevance;
}

/// <summary>Criteria for listing a series' chapters.</summary>
public sealed record ChapterQuery
{
    public IReadOnlyList<string> TranslatedLanguages { get; init; } = [];
    public IReadOnlyList<string> ScanlationGroups { get; init; } = [];
    public ChapterOrder Order { get; init; } = ChapterOrder.ChapterAscending;
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }
    public bool IncludeExternal { get; init; }

    /// <summary>Only chapters created at/after this instant (for incremental monitoring scans).</summary>
    public DateTimeOffset? CreatedSince { get; init; }
}
