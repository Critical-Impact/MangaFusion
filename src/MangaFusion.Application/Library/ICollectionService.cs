using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Library;

/// <summary>One of a user's collections in list form — metadata plus a member count and whether a
/// cover has been composed/uploaded yet. Feeds the Collections page grid and the dashboard.</summary>
public sealed record CollectionSummary(
    Guid Id,
    MediaKind Kind,
    string Name,
    string? Description,
    MemberSort MemberSort,
    CollectionDashboardFilter DashboardFilter,
    bool HasCover,
    int ItemCount,
    DateTimeOffset UpdatedAt);

/// <summary>A series inside a collection, already ordered by the collection's <see cref="MemberSort"/>.</summary>
public sealed record CollectionMember(
    Guid SeriesId,
    string Title,
    bool HasCover);

/// <summary>A single collection with its ordered members — feeds the collection detail page.</summary>
public sealed record CollectionDetail(
    Guid Id,
    MediaKind Kind,
    string Name,
    string? Description,
    MemberSort MemberSort,
    CollectionDashboardFilter DashboardFilter,
    bool CoverIsCustom,
    bool HasCover,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<CollectionMember> Members);

/// <summary>Manages a user's private, kind-scoped collections of library series. Every method is
/// scoped to <paramref name="userId"/> — a user can only see and mutate their own collections, so
/// ownership is enforced here rather than at the endpoint. Membership changes trigger a best-effort
/// cover regeneration unless the owner has uploaded a custom cover.</summary>
public interface ICollectionService
{
    Task<IReadOnlyList<CollectionSummary>> GetCollectionsAsync(
        Guid userId, MediaKind kind, CancellationToken ct = default);

    /// <summary>The collection with its ordered members, or null if it doesn't exist or isn't the
    /// user's. When <paramref name="forDashboard"/> is true, the collection's
    /// <see cref="CollectionDashboardFilter"/> is applied to the returned members (e.g. only unread
    /// downloaded series); the collection page passes false to always show every member.</summary>
    Task<CollectionDetail?> GetCollectionAsync(
        Guid userId, Guid id, bool forDashboard = false, CancellationToken ct = default);

    Task<CollectionSummary> CreateAsync(
        Guid userId, MediaKind kind, string name, string? description, CancellationToken ct = default);

    /// <summary>Updates name/description/sort/dashboard-filter. False if the collection isn't the user's.</summary>
    Task<bool> UpdateAsync(
        Guid userId, Guid id, string name, string? description, MemberSort memberSort,
        CollectionDashboardFilter dashboardFilter, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Adds a series (must exist and share the collection's kind). Idempotent — re-adding is a
    /// no-op. False if the collection isn't the user's or the series is missing/mismatched.</summary>
    Task<bool> AddSeriesAsync(Guid userId, Guid id, Guid seriesId, CancellationToken ct = default);

    Task<bool> RemoveSeriesAsync(Guid userId, Guid id, Guid seriesId, CancellationToken ct = default);

    /// <summary>Sets the manual order to the given series sequence and switches the sort to
    /// <see cref="MemberSort.Manual"/>. Series not currently members are ignored.</summary>
    Task<bool> ReorderAsync(
        Guid userId, Guid id, IReadOnlyList<Guid> orderedSeriesIds, CancellationToken ct = default);

    /// <summary>Which of the user's collections (ids) contain the given series — drives the
    /// add-to-collection picker's ticks on the series page.</summary>
    Task<IReadOnlySet<Guid>> GetMembershipAsync(Guid userId, Guid seriesId, CancellationToken ct = default);

    /// <summary>Absolute path to the collection's cover (custom or composed mosaic), or null if none.
    /// Resolved here because the stored path is relative to the kind's library root.</summary>
    Task<string?> GetCoverFileAsync(Guid userId, Guid id, CancellationToken ct = default);

    /// <summary>Stores an uploaded image as the collection's custom cover (overrides the auto-mosaic).
    /// False if the collection isn't the user's.</summary>
    Task<bool> SetCustomCoverAsync(
        Guid userId, Guid id, Stream image, string? contentType, CancellationToken ct = default);

    /// <summary>Drops the custom cover and reverts to the auto-generated mosaic.</summary>
    Task<bool> ClearCustomCoverAsync(Guid userId, Guid id, CancellationToken ct = default);
}
