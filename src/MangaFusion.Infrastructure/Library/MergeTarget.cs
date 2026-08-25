using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Picking the existing library series that an import/migration batch merges into.
///
/// Two rules apply to every merge target.
///
/// <b>1. The target must live in the same library as the batch.</b> Manga and comics share a database but
/// not a root directory, and a series' files are written under
/// <c>LibraryPaths.SeriesDirectory(series.Kind, …)</c>. A comic batch that merges into a same-titled manga
/// series does not fail. It writes the comic's chapters into the manga library, where the comic UI never
/// looks for them. Titles collide across the two libraries often, because an adaptation keeps the title of
/// its source, and that is when the auto-suggested match fires.
///
/// <b>2. The target must not come from a different metadata source.</b> A batch is matched against one
/// source (MangaUpdates for manga, ComicVine for comics, MangaDex for migration). A same-titled series that
/// came from another source gives no evidence that the two are the same work, and different works do share
/// a title. A target that is already linked to a <em>different</em> id on the batch's own source is
/// stronger evidence still: that is a different work, and the wizards must not offer it. A series with
/// only a local link stays eligible, because it is the hand-created series this feature exists for.
///
/// Both wizards route through here rather than each re-deriving the rules, so a rule cannot be added to
/// one and forgotten in the other. That is how rule 1 came to be missing from both.</summary>
internal static class MergeTarget
{
    /// <summary>Restricts <paramref name="series"/> to the targets a batch may merge into. The batch is
    /// matched against <paramref name="matchSourceId"/>. Pass its matched series id as
    /// <paramref name="matchedSourceSeriesId"/>, or null when the batch has no match yet. Does not filter
    /// by library: the callers scope the query by kind, which the API also does for its other uses.</summary>
    public static IQueryable<Series> Eligible(
        IQueryable<Series> series, string matchSourceId, string? matchedSourceSeriesId) =>
        series.Where(s =>
            !s.SourceLinks.Any(l =>
                l.SourceId != matchSourceId && l.SourceId != LocalSourceConstants.SourceId)
            && (matchedSourceSeriesId == null
                || !s.SourceLinks.Any(l =>
                    l.SourceId == matchSourceId && l.SourceSeriesId != matchedSourceSeriesId)));

    /// <summary>The library series to auto-suggest as a merge target: same title, <em>same library</em>,
    /// <em>same source</em>. <paramref name="lowerTitles"/> are already lowercased for the
    /// case-insensitive compare.</summary>
    public static async Task<Series?> FindByTitleAsync(
        AppDbContext db,
        MediaKind kind,
        IReadOnlyList<string> lowerTitles,
        string matchSourceId,
        string? matchedSourceSeriesId,
        CancellationToken ct)
    {
        if (lowerTitles.Count == 0)
        {
            return null;
        }

        var candidates = Eligible(db.Series, matchSourceId, matchedSourceSeriesId);
        return await candidates.FirstOrDefaultAsync(
            s => s.Kind == kind && lowerTitles.Contains(s.Title.ToLower()), ct);
    }

    /// <summary>Validates a merge target the user picked. Throws if the target does not exist, or if it
    /// breaks either rule above. A request that breaks a rule looks coherent ("merge into that series")
    /// and its result is silently wrong, so it must be refused and not corrected.</summary>
    public static async Task EnsureEligibleAsync(
        AppDbContext db,
        Guid seriesId,
        MediaKind kind,
        string matchSourceId,
        string? matchedSourceSeriesId,
        CancellationToken ct)
    {
        var target = await db.Series
            .Where(s => s.Id == seriesId)
            .Select(s => new
            {
                s.Kind,
                Links = s.SourceLinks.Select(l => new { l.SourceId, l.SourceSeriesId }).ToList(),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Target library series not found.");

        if (target.Kind != kind)
        {
            throw new InvalidOperationException(
                $"Target library series is in the {target.Kind} library; this batch imports into {kind}.");
        }

        var foreign = target.Links.FirstOrDefault(l =>
            l.SourceId != matchSourceId && l.SourceId != LocalSourceConstants.SourceId);
        if (foreign is not null)
        {
            throw new InvalidOperationException(
                $"Target library series comes from '{foreign.SourceId}'; this batch matches against " +
                $"'{matchSourceId}'. The two titles agree, but nothing shows that they are the same work. " +
                "Commit this as a new series instead.");
        }

        if (matchedSourceSeriesId is not null
            && target.Links.Any(l => l.SourceId == matchSourceId && l.SourceSeriesId != matchedSourceSeriesId))
        {
            throw new InvalidOperationException(
                $"Target library series is already linked to a different '{matchSourceId}' series, " +
                "so it is a different work. Commit this as a new series instead.");
        }
    }
}
