namespace MangaFusion.Web.Models;

/// <summary>A collection in list form for the Collections page grid and the dashboard. <see cref="Kind"/>
/// is lowercased ("manga"/"comic") to match the rest of the SPA; <see cref="MemberSort"/> is the enum
/// name (parsed case-insensitively on the way back in). <see cref="CoverUrl"/> carries a version stamp
/// so the browser refetches after the mosaic changes.</summary>
public sealed record CollectionDto(
    Guid Id,
    string Kind,
    string Name,
    string? Description,
    string MemberSort,
    string DashboardFilter,
    string? CoverUrl,
    int ItemCount,
    DateTimeOffset UpdatedAt);

public sealed record CollectionMemberDto(Guid SeriesId, string Title, string? CoverUrl);

public sealed record CollectionDetailDto(
    Guid Id,
    string Kind,
    string Name,
    string? Description,
    string MemberSort,
    string DashboardFilter,
    bool CoverIsCustom,
    string? CoverUrl,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CollectionMemberDto> Members);

public sealed record CreateCollectionRequest(string? Name, string? Description);

public sealed record UpdateCollectionRequest(
    string? Name, string? Description, string? MemberSort, string? DashboardFilter);

public sealed record ReorderCollectionRequest(Guid[]? SeriesIds);
