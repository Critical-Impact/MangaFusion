using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Writing;

/// <summary>One page image to include, read from a (typically temp) file on disk.</summary>
public sealed record PageFile(int Index, string ArchiveName, string SourcePath);

/// <summary>One chapter's worth of pages plus the metadata to describe it. A write with a single
/// segment produces a per-chapter artifact; multiple segments produce a volume/multi-chapter file.
/// <paramref name="PublishedAt"/> and <paramref name="SourceUrl"/> become ComicInfo's Year/Month/Day
/// and Web fields respectively.</summary>
public sealed record ChapterSegment(
    string? Number,
    string? Volume,
    string? Title,
    string Language,
    string? Group,
    IReadOnlyList<PageFile> Pages,
    DateTimeOffset? PublishedAt = null,
    string? SourceUrl = null);

/// <summary><paramref name="Genres"/> and <paramref name="OtherTags"/> become ComicInfo's Genre and
/// Tags elements respectively; <paramref name="Artists"/> becomes Penciller/CoverArtist.</summary>
public sealed record WriteRequest(
    string SeriesTitle,
    IReadOnlyList<string> Authors,
    IReadOnlyList<string> Genres,
    StorageFormat Format,
    string TargetDirectory,
    string FileBaseName,
    IReadOnlyList<ChapterSegment> Segments,
    IReadOnlyList<string>? Artists = null,
    IReadOnlyList<string>? OtherTags = null,
    string? Description = null,
    ContentRating ContentRating = ContentRating.Unknown,
    string? OriginalLanguage = null,
    IReadOnlyList<string>? AltTitles = null,
    /// <summary>Drives ComicInfo's <c>Manga</c> element, which readers use to pick page order. A comic
    /// must write <c>No</c> — writing <c>Yes</c> makes Komga/Kavita open it as a manga.</summary>
    MediaKind Kind = MediaKind.Manga);

public sealed record WriteResult(string Path, long SizeBytes, int PageCount, string Sha256);

/// <summary>Produces an on-disk artifact (CBZ or folder) from downloaded page files, plus a
/// ComicInfo.xml so the result is portable to Komga/Kavita/Mihon/etc.</summary>
public interface IChapterWriter
{
    StorageFormat Format { get; }

    Task<WriteResult> WriteAsync(WriteRequest request, IProgress<int>? progress = null, CancellationToken ct = default);
}
