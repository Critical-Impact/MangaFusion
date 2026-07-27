namespace MangaFusion.Application.Reading;

/// <summary>Everything the reader UI needs to open a chapter: page count, where to resume, reading
/// direction, and identifying metadata. Pages are fetched individually by index.</summary>
public sealed record ChapterManifest(
    Guid ChapterId,
    Guid ArtifactId,
    int PageCount,
    int StartPageIndex,
    string ReadingDirection,
    Guid SeriesId,
    string SeriesTitle,
    string? Number,
    string? Volume,
    string Language);

/// <summary>Previous/next <em>downloaded</em> chapter in the same language, for in-reader navigation.</summary>
public sealed record ReaderNeighbors(Guid? PrevChapterId, Guid? NextChapterId);

/// <summary>The next chapter to read in a series on the user's reading list — either the chapter they're
/// mid-way through (<see cref="PageIndex"/> &gt; 0) or the next unread downloaded chapter
/// (<see cref="PageIndex"/> == 0). Feeds the "Continue reading" rail. <paramref name="CoverPath"/> is
/// the on-disk relative cover path (null if none); the web layer maps it to a cover URL.</summary>
public sealed record ContinueReadingItem(
    Guid SeriesId,
    string SeriesTitle,
    string? CoverPath,
    Guid ChapterId,
    string? Number,
    string? Volume,
    string Language,
    int PageIndex,
    int PageCount,
    DateTimeOffset UpdatedAt);

/// <summary>A page's ETag the reader endpoint should emit for caching/304, with <see cref="Stream"/> and
/// <see cref="ContentType"/> populated only when the caller's <c>If-None-Match</c> didn't already match
/// (see <see cref="NotModified"/>) — the archive is never opened/decompressed for a cache hit.</summary>
public sealed record OpenPageResult(Stream? Stream, string? ContentType, string ETag)
{
    public bool NotModified => Stream is null;
}

/// <summary>Reads downloaded chapters for the in-app reader and tracks per-user progress.</summary>
public interface IReaderService
{
    /// <summary>Which reader a chapter needs, decided by its active artifact's <c>StorageFormat</c>:
    /// <c>"prose"</c> for an EPUB3 text artifact, <c>"image"</c> for CBZ/folder pages, or null if the
    /// chapter has no downloaded artifact. This is per-chapter, not per-library — a light-novel library
    /// can hold both — so the client dispatches to the text or image reader on this alone.</summary>
    Task<string?> GetReaderKindAsync(Guid chapterId, CancellationToken ct = default);

    Task<ChapterManifest?> GetManifestAsync(Guid userId, Guid chapterId, CancellationToken ct = default);

    /// <summary>Resolves the page's ETag first; if it matches <paramref name="ifNoneMatch"/> the archive
    /// is never opened (see <see cref="OpenPageResult.NotModified"/>).</summary>
    Task<OpenPageResult?> OpenPageAsync(
        Guid chapterId, int pageIndex, string? ifNoneMatch = null, CancellationToken ct = default);

    Task SaveProgressAsync(Guid userId, Guid chapterId, int pageIndex, bool completed, CancellationToken ct = default);

    /// <summary>Marks a chapter read (<paramref name="read"/> true → progress with <c>Completed</c>) or
    /// unread (false → the per-user progress row is removed entirely). Reader-agnostic — works for manga,
    /// prose and PDF chapters alike.</summary>
    Task SetChapterReadAsync(Guid userId, Guid chapterId, bool read, CancellationToken ct = default);

    Task<ReaderNeighbors> GetNeighborsAsync(Guid chapterId, CancellationToken ct = default);

    /// <summary><paramref name="kind"/> null = both libraries (the user's Home preference); otherwise
    /// scoped to the library they're currently in.</summary>
    Task<IReadOnlyList<ContinueReadingItem>> GetContinueReadingAsync(
        Guid userId, MediaKind? kind, int limit, CancellationToken ct = default);

    /// <summary>Whether the series is on the user's reading rail (read something or explicitly added,
    /// and not dismissed).</summary>
    Task<bool> IsReadingAsync(Guid userId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Adds the series to the reading rail (<paramref name="dismissed"/> false) or dismisses it
    /// (true), overriding any implicit "read something" membership.</summary>
    Task SetReadingAsync(Guid userId, Guid seriesId, bool dismissed, CancellationToken ct = default);
}
