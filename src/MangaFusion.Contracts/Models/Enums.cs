namespace MangaFusion.Contracts.Models;

/// <summary>What a source is able to do. Sources advertise their capabilities so callers and the
/// UI can adapt (e.g. hide a credentials form for a source that doesn't require auth).</summary>
[Flags]
public enum SourceCapabilities
{
    None = 0,
    Metadata = 1 << 0,
    Chapters = 1 << 1,
    Download = 1 << 2,
    RequiresAuth = 1 << 3,
}

/// <summary>Which flavour of library a source serves. Neutral copy of the domain enum, mapped at the
/// boundary like <see cref="ContentRating"/>/<see cref="PublicationStatus"/> — Contracts deliberately
/// doesn't depend on Domain.</summary>
public enum MediaKind
{
    Manga = 0,
    Comic = 1,
    LightNovel = 2,
}

/// <summary>Neutral content rating; each source maps its own values onto this.</summary>
public enum ContentRating
{
    Unknown = 0,
    Safe,
    Suggestive,
    Erotica,
    Pornographic,
}

/// <summary>Neutral publication status; each source maps its own values onto this.</summary>
public enum PublicationStatus
{
    Unknown = 0,
    Ongoing,
    Completed,
    Hiatus,
    Cancelled,
}

/// <summary>Requested ordering for a series search.</summary>
public enum SearchOrder
{
    Relevance = 0,
    LatestUploadedChapter,
    Title,
    Year,
    Rating,
    Followers,

    /// <summary>Newest titles first (by creation date).</summary>
    Newest,
}

/// <summary>Requested ordering for a chapter feed.</summary>
public enum ChapterOrder
{
    ChapterAscending = 0,
    ChapterDescending,
}

/// <summary>Image quality when resolving chapter pages.</summary>
public enum PageQuality
{
    Original = 0,
    DataSaver,
}
