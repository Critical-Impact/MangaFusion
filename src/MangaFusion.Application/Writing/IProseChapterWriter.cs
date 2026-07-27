using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Writing;

/// <summary>One chapter's worth of prose plus the metadata to describe it. <paramref name="Html"/> is the
/// chapter body as (already-trusted, writer-side) XHTML-compatible markup; <paramref name="Images"/> maps
/// an inline image's stable name (as referenced by an <c>&lt;img src&gt;</c> in <paramref name="Html"/>)
/// to the absolute path of its source file on disk. A single-segment write produces a per-chapter EPUB;
/// multiple segments produce a whole-volume/multi-chapter EPUB. Sibling to
/// <see cref="ChapterSegment"/> — the prose analogue of a page-image segment.</summary>
public sealed record ProseChapterSegment(
    string? Number,
    string? Volume,
    string? Title,
    string Language,
    string Html,
    IReadOnlyDictionary<string, string> Images,
    DateTimeOffset? PublishedAt = null,
    string? SourceUrl = null);

/// <summary>Everything needed to write one prose artifact. Sibling to <see cref="WriteRequest"/>; there is
/// no <c>StorageFormat</c> field because a prose artifact is always a real EPUB3.</summary>
public sealed record ProseWriteRequest(
    string SeriesTitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    string TargetDirectory,
    string FileBaseName,
    IReadOnlyList<ProseChapterSegment> Segments,
    IReadOnlyList<string>? Artists = null,
    IReadOnlyList<string>? OtherTags = null,
    string? Description = null,
    ContentRating ContentRating = ContentRating.Unknown);

/// <summary><paramref name="ChapterCount"/> is the number of chapter segments (= EPUB spine entries),
/// the prose analogue of <see cref="WriteResult"/>'s page count — a prose artifact's page count is
/// reinterpreted at chapter granularity (1 per chapter).</summary>
public sealed record ProseWriteResult(string Path, long SizeBytes, int ChapterCount, string Sha256);

/// <summary>Produces an on-disk EPUB3 artifact from prose chapter segments, portable to
/// Komga/Kavita/Calibre (all of which open EPUB as a novel natively). Parallel to, not an extension of,
/// <see cref="IChapterWriter"/>: a prose write emits a whole chapter's HTML + inline images, not page
/// images, so shoehorning a <c>Prose</c> branch into the image-page writer buys nothing.</summary>
public interface IProseChapterWriter
{
    Task<ProseWriteResult> WriteAsync(ProseWriteRequest request, CancellationToken ct = default);
}
