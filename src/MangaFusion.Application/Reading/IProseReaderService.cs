using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Reading;

/// <summary>Everything the prose reader UI needs to open a chapter: identifying metadata plus where to
/// resume (<see cref="StartScrollFraction"/>, 0..1 within the chapter's continuous scroll). The prose
/// analogue of <see cref="ChapterManifest"/> — no page count or reading direction (prose is a single
/// continuous-scroll column, not a paged image grid).</summary>
public sealed record ProseManifest(
    Guid ChapterId,
    Guid ArtifactId,
    Guid SeriesId,
    string SeriesTitle,
    string? Number,
    string? Volume,
    string Language,
    float StartScrollFraction,
    bool Completed);

/// <summary>A prose chapter's rendered body: server-sanitized HTML (safe to <c>innerHTML</c>) with inline
/// image <c>src</c>s already rewritten to absolute API URLs, plus a word count for the reading-time
/// estimate.</summary>
public sealed record ProseContent(string Html, int WordCount);

/// <summary>A light-novel PDF artifact on disk, to be streamed to the client's PDF.js reader.
/// <paramref name="ETag"/> is the artifact hash for caching/304.</summary>
public sealed record ProsePdfFile(string AbsolutePath, string ETag);

/// <summary>What the PDF.js reader needs to open a chapter: identifying metadata plus the saved resume
/// page (0-based). The PDF's real page count comes from PDF.js on the client, so it isn't here.</summary>
public sealed record ProsePdfManifest(
    Guid ChapterId,
    Guid SeriesId,
    string SeriesTitle,
    string? Number,
    string? Volume,
    string Language,
    int StartPage,
    bool Completed);

/// <summary>Reads prose chapters for the in-app text reader and records per-user progress. A new sibling
/// to <see cref="IReaderService"/>, deliberately not an extension of it — this keeps each interface's
/// contract single-purpose. Chapter navigation (<see cref="IReaderService.GetNeighborsAsync"/>),
/// the reading rail (<see cref="IReaderService.IsReadingAsync"/>/<see cref="IReaderService.SetReadingAsync"/>)
/// and Continue-reading are reused as-is from <see cref="IReaderService"/> — they're already
/// chapter/kind-agnostic, so they are not restated here.</summary>
public interface IProseReaderService
{
    Task<ProseManifest?> GetProseManifestAsync(Guid userId, Guid chapterId, CancellationToken ct = default);

    /// <summary>The chapter's sanitized body HTML + word count, or null if the chapter has no prose
    /// artifact.</summary>
    Task<ProseContent?> GetProseContentAsync(Guid chapterId, CancellationToken ct = default);

    /// <summary>Opens one inline image's bytes (with an ETag for caching/304), or null if the name isn't
    /// part of the chapter. Same shape as <see cref="IReaderService.OpenPageAsync"/>.</summary>
    Task<OpenPageResult?> OpenProseImageAsync(
        Guid chapterId, string imageName, string? ifNoneMatch = null, CancellationToken ct = default);

    Task SaveProseProgressAsync(
        Guid userId, Guid chapterId, float scrollFraction, bool completed, CancellationToken ct = default);

    /// <summary>Resolves the on-disk PDF for a light-novel chapter whose artifact is a stored-as-is PDF
    /// (<c>StorageFormat.Pdf</c>), for the PDF.js reader to stream. Null if the chapter has no PDF artifact.</summary>
    Task<ProsePdfFile?> ResolvePdfAsync(Guid chapterId, CancellationToken ct = default);

    Task<ProsePdfManifest?> GetPdfManifestAsync(Guid userId, Guid chapterId, CancellationToken ct = default);

    /// <summary>Saves the PDF reader's resume position as a 0-based page index (not clamped to the
    /// artifact's page window, which is 1 for a whole-volume PDF).</summary>
    Task SavePdfProgressAsync(Guid userId, Guid chapterId, int page, bool completed, CancellationToken ct = default);
}
