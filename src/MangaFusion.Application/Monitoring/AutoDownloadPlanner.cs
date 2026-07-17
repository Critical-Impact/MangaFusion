using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Monitoring;

public enum DecisionKind
{
    Download = 0,
    Replace = 1,
}

public sealed record MonitorDecision(Guid ChapterId, Guid ReleaseId, DecisionKind Kind);

/// <summary>
/// Pure decision engine for automatic downloads. Given a series' current state, decides which
/// chapters to download or replace, applying the group-preference grace rule:
/// <list type="bullet">
/// <item>preferred group available (or no preferences set) → download now;</item>
/// <item>only a non-preferred group available and preferences exist → wait <c>graceDays</c> from the
/// earliest release date, then take it (a preferred release appearing sooner wins on the next scan);</item>
/// <item>already downloaded but a more-preferred release exists → replace.</item>
/// </list>
/// No I/O and time is injected, so it is fully deterministic to test.
/// </summary>
public static class AutoDownloadPlanner
{
    public static IReadOnlyList<MonitorDecision> Plan(
        Series series,
        IReadOnlyCollection<string> wantedLanguages,
        int graceDays,
        DateTimeOffset now,
        ISet<Guid> chaptersWithPendingDownload)
    {
        if (wantedLanguages.Count == 0)
        {
            return [];
        }

        var languages = new HashSet<string>(wantedLanguages, StringComparer.OrdinalIgnoreCase);
        var preferred = series.PreferredGroups;
        var decisions = new List<MonitorDecision>();

        foreach (var chapter in series.Chapters)
        {
            if (!languages.Contains(chapter.Language) || chaptersWithPendingDownload.Contains(chapter.Id))
            {
                continue;
            }

            var best = LibrarySelectionService.SelectBest(chapter.Releases, preferred);
            if (best is null)
            {
                continue; // nothing downloadable (external only / no releases)
            }

            if (chapter.ActiveArtifactId is not null)
            {
                // Already downloaded: upgrade to a more-preferred group if one has appeared.
                if (LibrarySelectionService.IsUpgrade(chapter, best, preferred))
                {
                    decisions.Add(new MonitorDecision(chapter.Id, best.Id, DecisionKind.Replace));
                }

                continue;
            }

            var bestIsPreferred = LibrarySelectionService.Rank(best.GroupKey, preferred) < int.MaxValue;
            if (preferred.Count == 0 || bestIsPreferred)
            {
                decisions.Add(new MonitorDecision(chapter.Id, best.Id, DecisionKind.Download));
                continue;
            }

            // Only a non-preferred group is available and we have preferences → grace period.
            var anchor = chapter.Releases
                .Where(r => !r.IsExternal)
                .Select(r => r.PublishedAt ?? r.DiscoveredAt)
                .DefaultIfEmpty(now)
                .Min();

            if (now >= anchor.AddDays(graceDays))
            {
                decisions.Add(new MonitorDecision(chapter.Id, best.Id, DecisionKind.Download));
            }

            // else: defer to a later scan (a preferred release may still appear)
        }

        return decisions;
    }
}
