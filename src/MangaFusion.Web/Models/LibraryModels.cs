using MangaFusion.Application.Library;

namespace MangaFusion.Web.Models;

public sealed record AddSeriesRequest(string SourceId, string SourceSeriesId);

/// <summary>A batch "are these already in the library?" query for the browse grid.</summary>
public sealed record LibraryMembershipRequest(IReadOnlyList<AddSeriesRequest>? Refs);

/// <summary>One already-in-library source ref, carrying the library series id to link to.</summary>
public sealed record LibraryMembershipDto(string SourceId, string SourceSeriesId, Guid LibraryId);

public sealed record FollowRequest(string[]? Languages, bool AutoDownload);

public sealed record LibraryTitleDto(Guid Id, string Title);

public sealed record LibrarySeriesDto(
    Guid Id,
    string Title,
    string? CoverUrl,
    bool Followed,
    IReadOnlyList<string> Tags,
    int? Year,
    DateTimeOffset AddedAt,
    int ChapterCount,
    IReadOnlyList<string> Sources);

public sealed record LibrarySeriesDetailDto(
    Guid Id,
    string Title,
    IReadOnlyList<string> AltTitles,
    string? Description,
    string? CoverUrl,
    IReadOnlyList<AuthorRefDto> Authors,
    IReadOnlyList<TagInfo> Tags,
    string ContentRating,
    string Status,
    int? Year,
    IReadOnlyList<string> PreferredGroups,
    bool AutoDownload,
    int? GracePeriodDays,
    IReadOnlyList<string> SeriesLanguages,
    DateTimeOffset? LastScannedAt,
    string? SourceId,
    string? SourceName,
    string? SourceSeriesId,
    string? SiteUrl,
    bool Followed,
    bool FollowAutoDownload,
    IReadOnlyList<string> FollowLanguages,
    bool Reading,
    IReadOnlyList<LibraryChapterDto> Chapters,
    string SortMode,
    bool TitleLocked,
    bool YearLocked,
    bool DescriptionLocked,
    bool CoverLocked);

public sealed record LibraryChapterDto(
    Guid Id,
    string Language,
    string? Number,
    decimal? NumberSort,
    string? Volume,
    decimal? VolumeSort,
    string? Title,
    bool Downloaded,
    string? ActiveGroup,
    int PageIndex,
    bool Completed,
    /// <summary>The reader has any progress in this chapter — page turned, prose scrolled, or completed.
    /// Unlike <see cref="PageIndex"/>, this also catches prose (whose position lives in ScrollFraction with
    /// PageIndex left 0), so the UI can show read / in-progress / unread across every reader.</summary>
    bool Started,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseDto> Releases,
    bool CanEdit);

public sealed record UpdateChapterRequest(string? Number, string? Volume, string? Title);

public sealed record UpdateSeriesMetadataRequest(string Title, int? Year, string? Description);

public sealed record SetSortModeRequest(string SortMode);

public sealed record ReleaseDto(
    Guid Id,
    IReadOnlyList<string> Groups,
    string? GroupKey,
    bool IsExternal,
    DateTimeOffset? PublishedAt,
    int? PageCount);

public sealed record DownloadChapterRequest(Guid? ReleaseId);

public sealed record DownloadMissingRequest(string[]? Languages);

public sealed record SetGroupsRequest(string[]? Groups);

public sealed record SetPolicyRequest(int? GracePeriodDays, bool AutoDownload, string[]? Languages);

public sealed record SaveProgressRequest(int PageIndex, bool Completed);

/// <summary>Mark a chapter read (<c>true</c>) or unread (<c>false</c>) outside the reader.</summary>
public sealed record SetReadRequest(bool Read);

/// <summary>Prose reader progress: a 0..1 scroll fraction within the chapter's continuous-scroll column
/// (the prose analogue of <see cref="SaveProgressRequest"/>'s page index).</summary>
public sealed record SaveProseProgressRequest(float ScrollFraction, bool Completed);

/// <summary>PDF reader progress: the 0-based page the reader is on.</summary>
public sealed record SavePdfProgressRequest(int Page, bool Completed);

public sealed record DownloadDto(
    Guid Id,
    Guid SeriesId,
    Guid? ChapterId,
    string? Description,
    string Status,
    int PagesDone,
    int PagesTotal,
    string? Error,
    DateTimeOffset CreatedAt);
