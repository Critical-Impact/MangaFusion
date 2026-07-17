using MangaFusion.Contracts.Models;

namespace MangaFusion.Web.Models;

// Wire DTOs — decouple the HTTP surface from the Contracts models. Cover URLs are rewritten to the
// backend proxy so the browser never calls the source CDN directly.

public sealed record SourceSummaryDto(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Capabilities,
    bool RequiresAuth,
    bool Configured);

public sealed record CredentialFieldDto(string Name, string Label, bool Secret);

public sealed record TagDto(string Id, string Name, string Group);

public sealed record CredentialTestResult(bool Success);

/// <summary>An author/artist credit, carrying enough identity to link to the author page
/// (<c>/author/:sourceId/:authorId</c>). <see cref="SourceId"/>/<see cref="Id"/> are null when the
/// source doesn't expose per-author ids (e.g. local/manual imports) — the frontend falls back to a
/// name-based route in that case.</summary>
public sealed record AuthorRefDto(string? SourceId, string? Id, string Name);

/// <summary>A tag as attached to one series from a source-side (browse) lookup, carrying the source's
/// own tag id so it can link to <c>/genre/:sourceId/:tagId</c>. <see cref="Id"/> is null when the
/// source doesn't expose per-tag ids.</summary>
public sealed record SeriesTagDto(string? Id, string Name, string Group);

public sealed record SeriesDto(
    string SourceId,
    string SourceSeriesId,
    string Title,
    IReadOnlyList<string> AltTitles,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<AuthorRefDto> Authors,
    IReadOnlyList<AuthorRefDto> Artists,
    IReadOnlyList<SeriesTagDto> Tags,
    string ContentRating,
    string Status,
    int? Year,
    string? OriginalLanguage,
    IReadOnlyList<string> AvailableTranslatedLanguages,
    string? LastChapter,
    /// <summary>A real chapter/issue count when the source reports one (ComicVine); null otherwise.</summary>
    int? ChapterCount,
    /// <summary>The series' page on the source's own site, for linking out.</summary>
    string? SiteUrl);

public sealed record ChapterDto(
    string SourceId,
    string SourceChapterId,
    string? Volume,
    string? Number,
    string? Title,
    string Language,
    IReadOnlyList<string> ScanlationGroups,
    int? PageCount,
    DateTimeOffset? PublishedAt,
    bool IsExternal,
    string? ExternalUrl);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Total, int Limit, int Offset);

/// <summary>Manifest for the preview reader — a chapter's pages resolved live from the source,
/// keyed by source id + source chapter id (not an internal library chapter id).</summary>
public sealed record SourceChapterManifestDto(
    string SourceId, string SourceChapterId, int PageCount, string ReadingDirection);

internal static class ApiMapper
{
    public static SeriesDto ToDto(SourceSeries s) => new(
        s.SourceId,
        s.SourceSeriesId,
        s.Title,
        s.AltTitles,
        s.Description,
        ProxyCoverUrl(s.SourceId, s.CoverUrl),
        ToAuthorRefs(s.SourceId, s.Authors, s.AuthorRefs),
        ToAuthorRefs(s.SourceId, s.Artists, s.ArtistRefs),
        ToTags(s.Tags, s.TagRefs),
        s.ContentRating.ToString(),
        s.Status.ToString(),
        s.Year,
        s.OriginalLanguage,
        s.AvailableTranslatedLanguages,
        s.LastChapter,
        s.ChapterCount,
        s.SiteUrl);

    public static ChapterDto ToDto(SourceChapter c) => new(
        c.SourceId,
        c.SourceChapterId,
        c.Volume,
        c.Number,
        c.Title,
        c.Language,
        c.ScanlationGroups,
        c.PageCount,
        c.PublishedAt,
        c.IsExternal,
        c.ExternalUrl);

    private static List<AuthorRefDto> ToAuthorRefs(
        string sourceId, IReadOnlyList<string> names, IReadOnlyList<SourceAuthorRef> refs) =>
        refs.Count > 0
            ? refs.Select(r => new AuthorRefDto(sourceId, r.Id, r.Name)).ToList()
            : names.Select(n => new AuthorRefDto(null, null, n)).ToList();

    private static List<SeriesTagDto> ToTags(
        IReadOnlyList<string> names, IReadOnlyList<SourceTagRef> refs) =>
        refs.Count > 0
            ? refs.Select(r => new SeriesTagDto(r.Id, r.Name, r.Group)).ToList()
            : names.Select(n => new SeriesTagDto(null, n, "other")).ToList();

    public static string? ProxyCoverUrl(string sourceId, string? coverUrl) =>
        coverUrl is null ? null : $"/api/sources/{sourceId}/cover?url={Uri.EscapeDataString(coverUrl)}";
}
