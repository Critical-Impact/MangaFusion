using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Per-user, kind-scoped collections of library series. Every method is scoped to a user id,
/// so a user only ever sees or mutates their own collections. Membership/sort changes trigger a
/// best-effort cover regeneration unless the owner uploaded a custom cover
/// (<see cref="CollectionCoverComposer"/>).</summary>
public sealed class CollectionService(
    AppDbContext db, LibraryPaths paths, CollectionCoverComposer composer) : ICollectionService
{
    public async Task<IReadOnlyList<CollectionSummary>> GetCollectionsAsync(
        Guid userId, MediaKind kind, CancellationToken ct = default) =>
        await db.Collections
            .Where(c => c.UserId == userId && c.Kind == kind)
            .OrderBy(c => c.Name)
            .Select(c => new CollectionSummary(
                c.Id, c.Kind, c.Name, c.Description, c.MemberSort, c.DashboardFilter,
                c.CoverPath != null, c.Items.Count, c.UpdatedAt))
            .ToListAsync(ct);

    public async Task<CollectionDetail?> GetCollectionAsync(
        Guid userId, Guid id, bool forDashboard = false, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return null;
        }

        var ordered = OrderMembers(collection).ToList();

        // The dashboard rail honours the collection's filter; the collection page (forDashboard=false)
        // always shows every member.
        if (forDashboard && collection.DashboardFilter == CollectionDashboardFilter.Unread)
        {
            var available = await UnreadDownloadedSeriesAsync(userId, ordered.Select(i => i.SeriesId).ToList(), ct);
            ordered = ordered.Where(i => available.Contains(i.SeriesId)).ToList();
        }

        var members = ordered
            .Select(i => new CollectionMember(i.SeriesId, i.Series.Title, i.Series.CoverPath != null))
            .ToList();

        return new CollectionDetail(
            collection.Id, collection.Kind, collection.Name, collection.Description, collection.MemberSort,
            collection.DashboardFilter, collection.CoverIsCustom, collection.CoverPath != null,
            collection.UpdatedAt, members);
    }

    /// <summary>Of the given series, those with at least one downloaded chapter (an active artifact)
    /// the user hasn't finished reading. One query, not per-series.</summary>
    private async Task<HashSet<Guid>> UnreadDownloadedSeriesAsync(
        Guid userId, IReadOnlyList<Guid> seriesIds, CancellationToken ct)
    {
        if (seriesIds.Count == 0)
        {
            return [];
        }

        var ids = await db.Chapters
            .Where(c => seriesIds.Contains(c.SeriesId)
                && c.ActiveArtifactId != null
                && !db.ReadingProgress.Any(p => p.UserId == userId && p.ChapterId == c.Id && p.Completed))
            .Select(c => c.SeriesId)
            .Distinct()
            .ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<CollectionSummary> CreateAsync(
        Guid userId, MediaKind kind, string name, string? description, CancellationToken ct = default)
    {
        var collection = new Collection
        {
            UserId = userId,
            Kind = kind,
            Name = name.Trim(),
            Description = Normalize(description),
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(ct);

        return new CollectionSummary(
            collection.Id, collection.Kind, collection.Name, collection.Description, collection.MemberSort,
            collection.DashboardFilter, false, 0, collection.UpdatedAt);
    }

    public async Task<bool> UpdateAsync(
        Guid userId, Guid id, string name, string? description, MemberSort memberSort,
        CollectionDashboardFilter dashboardFilter, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return false;
        }

        var sortChanged = collection.MemberSort != memberSort;
        collection.Name = name.Trim();
        collection.Description = Normalize(description);
        collection.MemberSort = memberSort;
        collection.DashboardFilter = dashboardFilter;
        collection.UpdatedAt = DateTimeOffset.UtcNow;

        // The mosaic uses the top members in display order, so a sort change can change the cover.
        if (sortChanged)
        {
            await RegenerateCoverAsync(collection, ct);
        }

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (collection is null)
        {
            return false;
        }

        db.Collections.Remove(collection); // cascades to CollectionItems
        await db.SaveChangesAsync(ct);

        // Best-effort cleanup of the cover directory; a leftover folder is harmless.
        try
        {
            var dir = paths.CollectionDirectory(collection.Kind, collection.Id);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // ignored — orphaned cover files never affect correctness
        }

        return true;
    }

    public async Task<bool> AddSeriesAsync(Guid userId, Guid id, Guid seriesId, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return false;
        }

        var series = await db.Series.FirstOrDefaultAsync(s => s.Id == seriesId && s.Kind == collection.Kind, ct);
        if (series is null)
        {
            return false;
        }

        if (collection.Items.Any(i => i.SeriesId == seriesId))
        {
            return true; // idempotent
        }

        var nextPosition = collection.Items.Count == 0 ? 0 : collection.Items.Max(i => i.Position) + 1;
        // Explicit Add (with CollectionId set) guarantees the Added state for a client-set Guid key;
        // EF's relationship fixup then also puts it on collection.Items, so we don't add it twice.
        db.CollectionItems.Add(new CollectionItem
        {
            CollectionId = collection.Id,
            SeriesId = seriesId,
            Series = series,
            Position = nextPosition,
        });
        collection.UpdatedAt = DateTimeOffset.UtcNow;

        await RegenerateCoverAsync(collection, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RemoveSeriesAsync(Guid userId, Guid id, Guid seriesId, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return false;
        }

        var item = collection.Items.FirstOrDefault(i => i.SeriesId == seriesId);
        if (item is null)
        {
            return true; // already absent
        }

        collection.Items.Remove(item);
        db.CollectionItems.Remove(item);
        collection.UpdatedAt = DateTimeOffset.UtcNow;

        await RegenerateCoverAsync(collection, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ReorderAsync(
        Guid userId, Guid id, IReadOnlyList<Guid> orderedSeriesIds, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return false;
        }

        var order = orderedSeriesIds
            .Select((sid, index) => (sid, index))
            .ToDictionary(x => x.sid, x => x.index);

        // Members named in the request take the given order; any not mentioned sink to the end,
        // keeping their prior relative order.
        var fallback = orderedSeriesIds.Count;
        foreach (var item in collection.Items)
        {
            item.Position = order.TryGetValue(item.SeriesId, out var pos) ? pos : fallback + item.Position;
        }

        collection.MemberSort = MemberSort.Manual;
        collection.UpdatedAt = DateTimeOffset.UtcNow;

        await RegenerateCoverAsync(collection, ct);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<IReadOnlySet<Guid>> GetMembershipAsync(
        Guid userId, Guid seriesId, CancellationToken ct = default)
    {
        var ids = await (
            from item in db.CollectionItems
            join collection in db.Collections on item.CollectionId equals collection.Id
            where item.SeriesId == seriesId && collection.UserId == userId
            select item.CollectionId).ToListAsync(ct);

        return ids.ToHashSet();
    }

    public async Task<string?> GetCoverFileAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var row = await db.Collections
            .Where(c => c.Id == id && c.UserId == userId)
            .Select(c => new { c.Kind, c.CoverPath })
            .FirstOrDefaultAsync(ct);

        return row?.CoverPath is null ? null : paths.Absolute(row.Kind, row.CoverPath);
    }

    public async Task<bool> SetCustomCoverAsync(
        Guid userId, Guid id, Stream image, string? contentType, CancellationToken ct = default)
    {
        var collection = await db.Collections.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);
        if (collection is null)
        {
            return false;
        }

        var relative = await composer.StoreCustomAsync(collection.Kind, collection.Id, image, ct);
        if (relative is null)
        {
            return false; // not a valid image
        }

        collection.CoverPath = relative;
        collection.CoverIsCustom = true;
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> ClearCustomCoverAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var collection = await LoadWithMembersAsync(userId, id, ct);
        if (collection is null)
        {
            return false;
        }

        if (collection.CoverIsCustom)
        {
            try
            {
                var custom = Path.Combine(paths.CollectionDirectory(collection.Kind, collection.Id), "cover-custom.jpg");
                if (File.Exists(custom))
                {
                    File.Delete(custom);
                }
            }
            catch
            {
                // ignored
            }

            collection.CoverIsCustom = false;
        }

        await RegenerateCoverAsync(collection, ct);
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private Task<Collection?> LoadWithMembersAsync(Guid userId, Guid id, CancellationToken ct) =>
        db.Collections
            .Include(c => c.Items)
            .ThenInclude(i => i.Series)
            .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

    /// <summary>Recomposes the auto mosaic from the current top members. No-op when the cover is custom.
    /// Requires <paramref name="collection"/> to have Items+Series loaded. Sets <c>CoverPath</c> in
    /// memory (caller saves); it may become null when no member has a cover, yielding a placeholder.</summary>
    private async Task RegenerateCoverAsync(Collection collection, CancellationToken ct)
    {
        if (collection.CoverIsCustom)
        {
            return;
        }

        var files = OrderMembers(collection)
            .Where(i => i.Series.CoverPath != null)
            .Select(i => paths.Absolute(collection.Kind, i.Series.CoverPath!))
            .ToList();

        collection.CoverPath = await composer.ComposeAsync(collection.Kind, collection.Id, files, ct);
    }

    private static IEnumerable<CollectionItem> OrderMembers(Collection collection) => collection.MemberSort switch
    {
        MemberSort.TitleAsc => collection.Items.OrderBy(i => i.Series.Title, StringComparer.OrdinalIgnoreCase),
        MemberSort.TitleDesc => collection.Items.OrderByDescending(i => i.Series.Title, StringComparer.OrdinalIgnoreCase),
        MemberSort.RecentlyAdded => collection.Items.OrderByDescending(i => i.AddedAt),
        MemberSort.Year => collection.Items.OrderByDescending(i => i.Series.Year ?? int.MinValue)
            .ThenBy(i => i.Series.Title, StringComparer.OrdinalIgnoreCase),
        _ => collection.Items.OrderBy(i => i.Position).ThenBy(i => i.AddedAt),
    };

    private static string? Normalize(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
