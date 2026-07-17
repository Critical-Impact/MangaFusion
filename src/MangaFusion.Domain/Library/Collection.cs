namespace MangaFusion.Domain.Library;

/// <summary>A user's private, curated grouping of library series (like a Plex collection / a personal
/// list). Per-user and kind-scoped: a collection belongs to one <see cref="MediaKind"/> and only its
/// owner sees it. Membership order is chosen via <see cref="MemberSort"/> — <c>Manual</c> uses each
/// <see cref="CollectionItem.Position"/>; the presets are computed from series metadata at query time.</summary>
public class Collection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owner. Collections are private to this user.</summary>
    public Guid UserId { get; set; }

    /// <summary>Which half of the library this collection lives in. There's no path back to a series
    /// (a collection can be empty), so it carries its own copy — see <see cref="MediaKind"/>.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>How members are ordered on the collection's page.</summary>
    public MemberSort MemberSort { get; set; } = MemberSort.Manual;

    /// <summary>Which members surface on the Home dashboard rail (the collection page always shows all).</summary>
    public CollectionDashboardFilter DashboardFilter { get; set; } = CollectionDashboardFilter.All;

    /// <summary>Cached local cover path, relative to the (kind's) library root; null until composed.
    /// Mirrors <see cref="Series.CoverPath"/>.</summary>
    public string? CoverPath { get; set; }

    /// <summary>True when the owner uploaded a custom cover — the auto-mosaic must not overwrite it.</summary>
    public bool CoverIsCustom { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<CollectionItem> Items { get; set; } = [];
}
