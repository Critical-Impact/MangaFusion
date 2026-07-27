namespace MangaFusion.Application.Library;

/// <summary>Shared identifier for the built-in local/manual "source". Local series are created and
/// imported through <see cref="ILocalLibraryService"/> rather than a remote provider, and are not
/// registered in the source registry (nothing fetches them).</summary>
public static class LocalSourceConstants
{
    public const string SourceId = "local";
}

/// <summary>Hand-entered metadata for a manually-created series.</summary>
public sealed record LocalSeriesMetadata(
    string Title,
    IReadOnlyList<string>? AltTitles,
    IReadOnlyList<string>? Authors,
    IReadOnlyList<string>? Tags,
    string? Description,
    string? ContentRating,
    string? Status,
    int? Year,
    string? OriginalLanguage,
    string? CoverFileName,
    /// <summary>Which library to create the series in. Only honoured on create — a series can't change
    /// libraries later, since its chapters, tags and progress all hang off the kind it was created with.</summary>
    MediaKind Kind = MediaKind.Manga);

/// <summary>An importable file/folder discovered in the local inbox. <paramref name="Prose"/> is set when
/// the file was detected as reflowable text (light-novel prose) rather than page images — it drives which
/// import controls the UI shows (a prose file imports as one chapter; an image file splits by page
/// count). <paramref name="PageCount"/> is 0 for prose (meaningless there).</summary>
public sealed record InboxItem(string Name, string Kind, int PageCount, long SizeBytes, bool Prose = false);

/// <summary>A local series, for the import target picker.</summary>
public sealed record LocalSeriesSummary(Guid Id, string Title);

/// <summary>One chapter to carve out of an imported file. <see cref="PageCount"/> is that chapter's
/// slice of the file; for a single-chapter import it may be 0 to mean "the whole file".</summary>
public sealed record LocalChapterSpec(string? Number, string? Volume, string? Title, int PageCount);

/// <summary>Maps one inbox file to one or more chapters of a series in a given language.</summary>
public sealed record LocalImportRequest(string FileName, string Language, IReadOnlyList<LocalChapterSpec> Chapters);

/// <summary>Creates manually-curated series and imports local CBZ/folder files as their chapters.
/// The reader (M4) then serves them like any downloaded chapter.</summary>
public interface ILocalLibraryService
{
    Task<Guid> CreateSeriesAsync(LocalSeriesMetadata metadata, CancellationToken ct = default);

    Task UpdateSeriesAsync(Guid seriesId, LocalSeriesMetadata metadata, CancellationToken ct = default);

    /// <summary>Local series in one library — the import-target picker. Scoped by kind so a comic can't be
    /// imported into a manga series.</summary>
    Task<IReadOnlyList<LocalSeriesSummary>> ListSeriesAsync(MediaKind kind, CancellationToken ct = default);

    /// <summary>The inbox is split per library on disk, so listing it needs to know which one to read.</summary>
    Task<IReadOnlyList<InboxItem>> ListInboxAsync(MediaKind kind, CancellationToken ct = default);

    /// <summary>Imports one inbox file as chapters of the series. Returns the number of chapters added.</summary>
    Task<int> ImportAsync(Guid seriesId, LocalImportRequest request, CancellationToken ct = default);
}
