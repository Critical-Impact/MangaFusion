using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Picking the existing library series that an import/migration batch merges into.
///
/// The invariant both wizards need and neither used to enforce: <b>a merge target must live in the same
/// library as the batch</b>. Manga and comics share a database but not a root directory, and a series'
/// files are written under <c>LibraryPaths.SeriesDirectory(series.Kind, …)</c> — so merging a comic batch
/// into a same-titled manga series doesn't fail, it silently writes the comic's chapters into the manga
/// library, where the comic UI will never look for them. Same-title collisions across the two libraries are
/// not exotic (adaptations share their source's title), which is exactly when the auto-suggested match
/// fires.
///
/// Both wizards route through here rather than each re-deriving the rule, so the guard can't be added to
/// one and forgotten in the other — which is how it was missing from both.</summary>
internal static class MergeTarget
{
    /// <summary>The library series to auto-suggest as a merge target: same title, <em>same library</em>.
    /// <paramref name="lowerTitles"/> are already lowercased for the case-insensitive compare.</summary>
    public static async Task<Series?> FindByTitleAsync(
        AppDbContext db, MediaKind kind, IReadOnlyList<string> lowerTitles, CancellationToken ct)
    {
        if (lowerTitles.Count == 0)
        {
            return null;
        }

        return await db.Series.FirstOrDefaultAsync(
            s => s.Kind == kind && lowerTitles.Contains(s.Title.ToLower()), ct);
    }

    /// <summary>Validates a merge target the user picked explicitly. Throws if it doesn't exist, or if it's
    /// in the other library — the latter is a coherent-looking request ("merge into that series") whose
    /// result would be silently wrong, so it has to be refused rather than clamped.</summary>
    public static async Task EnsureInLibraryAsync(
        AppDbContext db, Guid seriesId, MediaKind kind, CancellationToken ct)
    {
        var target = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => new { s.Kind })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Target library series not found.");

        if (target.Kind != kind)
        {
            throw new InvalidOperationException(
                $"Target library series is in the {target.Kind} library; this batch imports into {kind}.");
        }
    }
}
