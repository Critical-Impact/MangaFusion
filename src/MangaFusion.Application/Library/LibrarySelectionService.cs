using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Library;

/// <summary>Pure release-selection logic: given a chapter's releases and the series' ordered group
/// preference, choose which release to download and decide whether a candidate is an upgrade.</summary>
public static class LibrarySelectionService
{
    /// <summary>Preference rank of a group (0 = most preferred); unlisted groups rank last.</summary>
    public static int Rank(string? groupKey, IReadOnlyList<string> preferredGroups)
    {
        if (groupKey is null)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < preferredGroups.Count; i++)
        {
            if (string.Equals(preferredGroups[i], groupKey, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    /// <summary>Best downloadable release: most-preferred group, newest as a tiebreak; skips external.</summary>
    public static ChapterRelease? SelectBest(
        IEnumerable<ChapterRelease> releases, IReadOnlyList<string> preferredGroups) =>
        releases
            .Where(r => !r.IsExternal)
            .OrderBy(r => Rank(r.GroupKey, preferredGroups))
            .ThenByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();

    /// <summary>True if downloading <paramref name="candidate"/> is a group upgrade over the chapter's
    /// current release (or the chapter isn't downloaded yet).</summary>
    public static bool IsUpgrade(Chapter chapter, ChapterRelease candidate, IReadOnlyList<string> preferredGroups)
    {
        if (chapter.ActiveReleaseId is null)
        {
            return true;
        }

        var active = chapter.Releases.FirstOrDefault(r => r.Id == chapter.ActiveReleaseId);
        return active is null
            || Rank(candidate.GroupKey, preferredGroups) < Rank(active.GroupKey, preferredGroups);
    }
}
