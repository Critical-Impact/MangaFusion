using MangaFusion.Application.Library;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ContentRating = MangaFusion.Contracts.Models.ContentRating;
using ChapterQuery = MangaFusion.Contracts.Models.ChapterQuery;
using SearchOrder = MangaFusion.Contracts.Models.SearchOrder;
using SearchQuery = MangaFusion.Contracts.Models.SearchQuery;
using SourceChapter = MangaFusion.Contracts.Models.SourceChapter;
using SourceSeries = MangaFusion.Contracts.Models.SourceSeries;

namespace MangaFusion.Infrastructure.Library;

/// <summary>One file's resolved match/dedup outcome, ready to persist onto a
/// <see cref="MigrationItem"/>.</summary>
public sealed record MatchedItem(
    ScannedFile File,
    string? MatchedSourceChapterId,
    string? MatchedGroup,
    MigrationItemDisposition Disposition,
    bool IsWinner,
    string? FlagReason,
    string? ResolvedNumber = null,
    string? ResolvedTitle = null);

/// <summary>The full outcome of matching one inbox series folder against MangaDex.</summary>
public sealed record MatchResult(
    string? ComicInfoSeriesTitle,
    string? MatchedSourceId,
    string? MatchedSourceSeriesId,
    string? MatchedTitle,
    MigrationRegime Regime,
    double Confidence,
    IReadOnlyList<string> GroupRanking,
    IReadOnlyList<MatchedItem> Items,
    string? ConflictReason);

/// <summary>Matches one scanned inbox folder against MangaDex: picks the series by title/alt-title
/// similarity, resolves each local file to a feed chapter by its UUID-prefix, detects whether the
/// series' chapters are still live or have been purged from the feed (e.g. licensed &amp; delisted),
/// ranks scanlation groups by frequency, and dedups multiple local copies of the same chapter
/// number. Pure decision logic over one MangaDex search + at most one feed fetch — no DB access.</summary>
public sealed class MigrationMatcher(ISourceRegistry registry, ILogger<MigrationMatcher>? logger = null)
{
    // Optional so existing hand-constructed test doubles don't need updating; DI always supplies one.
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public const string SourceId = "mangadex";

    // User-set: title similarity + prefix-overlap bar for an unattended auto-commit. Deliberately
    // strict (effectively "exact, or near-exact") — a merely-plausible ("contains") match must never
    // auto-accept, since a wrong match silently imports real chapters under the wrong series. See the
    // "Sono Bisque Doll wa Koi o Suru" incident: with a lenient 0.6 bar, an adult doujinshi spinoff
    // matched at 0.75 (its alt-title contained the real title as a substring) and auto-committed
    // before the correct series — ranked just outside the old, too-small search window — was ever
    // even considered.
    private const double TitleMatchThreshold = 0.9;
    private const double LiveThreshold = 0.7;
    private const double PurgedThreshold = 0.3;

    // MangaDex's max page size for /manga search. Popular titles can have dozens of doujinshi/parody
    // "spinoff" entries whose titles/alt-titles contain the real title as a prefix — those can
    // outrank the real series in MangaDex's own relevance ordering and, at a small limit, crowd it
    // out of the candidate pool entirely (verified: the real series ranked 11th of 21 candidates for
    // the case above). Fetching the full page is one extra-large response, not extra requests.
    private const int SearchLimit = 100;

    private const int FeedPageSize = 500;
    private const int MaxFeedPages = 20;

    public async Task<MatchResult> MatchAsync(ScannedSeriesFolder folder, CancellationToken ct)
    {
        var comicInfoTitle = MostCommonTitle(folder.Files) ?? folder.FolderName;
        _logger.LogDebug(
            "Migration match: searching MangaDex for {Title} ({FileCount} local files in {Folder}).",
            comicInfoTitle, folder.Files.Count, folder.FolderName);

        var metadata = registry.GetMetadataSource(SourceId);
        var search = await metadata.SearchAsync(
            new SearchQuery
            {
                Text = comicInfoTitle,
                Limit = SearchLimit,
                ContentRatings =
                [
                    ContentRating.Safe, ContentRating.Suggestive, ContentRating.Erotica, ContentRating.Pornographic,
                ],
                Order = SearchOrder.Relevance,
            }, ct);

        var (best, score) = PickBestCandidate(comicInfoTitle, search.Items);
        _logger.LogDebug(
            "Migration match: {Candidates} candidate(s) for {Title}; best is {Best} at {Score:P0} title similarity.",
            search.Items.Count, comicInfoTitle, best?.Title ?? "(none)", score);

        if (best is null || score < TitleMatchThreshold)
        {
            return new MatchResult(
                comicInfoTitle, null, null, null, MigrationRegime.Unmatched, 0, [],
                folder.Files.Select(f => new MatchedItem(f, null, null, MigrationItemDisposition.Unresolved, false,
                    "No confident MangaDex match for this series.")).ToList(),
                $"No confident MangaDex match for \"{comicInfoTitle}\" (best candidate: " +
                $"{(best is null ? "none" : $"\"{best.Title}\" at {score:P0} title similarity")}).");
        }

        return await ResolveAgainstSeriesAsync(folder, comicInfoTitle, best.SourceSeriesId, best.Title, ct);
    }

    /// <summary>Re-resolves a folder against a specific MangaDex series id, bypassing search —
    /// used when the user manually picks (or corrects) the match during review.</summary>
    public async Task<MatchResult> MatchAgainstSeriesAsync(
        ScannedSeriesFolder folder, string sourceSeriesId, CancellationToken ct)
    {
        var comicInfoTitle = MostCommonTitle(folder.Files) ?? folder.FolderName;
        var metadata = registry.GetMetadataSource(SourceId);
        var series = await metadata.GetSeriesAsync(sourceSeriesId, ct)
            ?? throw new InvalidOperationException($"MangaDex series '{sourceSeriesId}' not found.");

        return await ResolveAgainstSeriesAsync(folder, comicInfoTitle, sourceSeriesId, series.Title, ct);
    }

    private async Task<MatchResult> ResolveAgainstSeriesAsync(
        ScannedSeriesFolder folder, string comicInfoTitle, string sourceSeriesId, string matchedTitle,
        CancellationToken ct)
    {
        var chapters = registry.GetChapterSource(SourceId);
        var feed = await FetchEnFeedAsync(chapters, sourceSeriesId, ct);
        var byPrefix = BuildPrefixIndex(feed);
        _logger.LogDebug(
            "Migration match: fetched {FeedCount} en feed chapter(s) for {Series} ({SourceSeriesId}).",
            feed.Count, matchedTitle, sourceSeriesId);

        var resolved = folder.Files.Select(f => Resolve(f, byPrefix)).ToList();
        var matchedCount = resolved.Count(r => r.MatchedSourceChapterId is not null);
        var confidence = folder.Files.Count == 0 ? 0 : (double)matchedCount / folder.Files.Count;

        var groupRanking = RankGroups(resolved);
        var deduped = Dedup(resolved, groupRanking, out var hadHeuristicTies);

        var regime = confidence >= LiveThreshold ? MigrationRegime.Live
            : confidence <= PurgedThreshold ? MigrationRegime.Purged
            : MigrationRegime.Mixed;
        _logger.LogDebug(
            "Migration match: {Series} resolved as {Regime} ({Confidence:P0} prefix overlap); " +
            "group ranking: {Groups}.",
            matchedTitle, regime, confidence, groupRanking.Count == 0 ? "(none)" : string.Join(", ", groupRanking));

        var hasAmbiguous = deduped.Any(i => i.Disposition == MigrationItemDisposition.Unresolved);
        var missingOpener = IsMissingOpeningChapter(folder.Files, feed);

        var reasons = new List<string>();
        if (regime == MigrationRegime.Mixed)
        {
            reasons.Add($"Series is partially purged from MangaDex ({confidence:P0} of local chapters still " +
                        "found in the feed) — group ranking may be incomplete.");
        }

        if (hasAmbiguous)
        {
            reasons.Add("One or more files' UUID prefixes matched multiple feed chapters — resolve manually.");
        }

        if (hadHeuristicTies)
        {
            reasons.Add("Some chapters had multiple local copies with no group data to rank them; a best " +
                        "guess (most pages, then title) was pre-selected — review before committing.");
        }

        if (missingOpener)
        {
            reasons.Add("No chapter 1 (or 0) found locally or on MangaDex — this series may be starting " +
                        "mid-run; check whether earlier chapters exist before importing.");
        }

        return new MatchResult(
            comicInfoTitle, SourceId, sourceSeriesId, matchedTitle, regime, confidence,
            groupRanking, deduped, reasons.Count == 0 ? null : string.Join(" ", reasons));
    }

    /// <summary>True when the local files use numbered chapters at all but neither a usable local
    /// copy nor a downloadable (non-external) MangaDex release covers the opening chapter — e.g. a
    /// folder that starts at "Chapter 20", or (as verified against real sample data) a series whose
    /// only chapter 1 anywhere is an external retail redirect. Doesn't block the rest of the series
    /// from importing — just flags it for review.</summary>
    private static bool IsMissingOpeningChapter(IReadOnlyList<ScannedFile> localFiles, IReadOnlyList<SourceChapter> feed)
    {
        var hasNumbering = localFiles.Any(f => ChapterNumber.Normalize(f.Number).Sort is not null);
        if (!hasNumbering)
        {
            return false; // e.g. an all-oneshots folder — no numbering scheme to be missing an opener from
        }

        bool IsOpener(string? number) =>
            number is not null && ChapterNumber.Normalize(number).Key is "1" or "0";

        var hasLocalOpener = localFiles.Any(f => f.IntegrityFailureReason is null && IsOpener(f.Number));
        var hasFeedOpener = feed.Any(c => !c.IsExternal && IsOpener(c.Number));
        return !hasLocalOpener && !hasFeedOpener;
    }

    // --- Series title matching ---------------------------------------------------------------

    private static (SourceSeries? Best, double Score) PickBestCandidate(
        string comicInfoTitle, IReadOnlyList<SourceSeries> candidates)
    {
        SourceSeries? best = null;
        var bestScore = 0.0;
        foreach (var candidate in candidates)
        {
            var score = TitleMatching.Score(comicInfoTitle, [candidate.Title, .. candidate.AltTitles]);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return (best, bestScore);
    }

    private static string? MostCommonTitle(IReadOnlyList<ScannedFile> files) =>
        files.Select(f => f.ComicInfoSeriesTitle)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault();

    // --- Feed fetch + prefix matching --------------------------------------------------------

    private static async Task<List<SourceChapter>> FetchEnFeedAsync(
        IChapterSource source, string sourceSeriesId, CancellationToken ct)
    {
        var all = new List<SourceChapter>();
        var offset = 0;
        for (var page = 0; page < MaxFeedPages; page++)
        {
            var result = await source.GetChaptersAsync(
                sourceSeriesId,
                new ChapterQuery
                {
                    TranslatedLanguages = ["en"], Limit = FeedPageSize, Offset = offset, IncludeExternal = true,
                }, ct);

            all.AddRange(result.Items);
            offset += FeedPageSize;
            if (result.Items.Count < FeedPageSize || offset >= result.Total)
            {
                break;
            }
        }

        return all;
    }

    private static Dictionary<string, List<SourceChapter>> BuildPrefixIndex(IReadOnlyList<SourceChapter> feed)
    {
        var index = new Dictionary<string, List<SourceChapter>>(StringComparer.OrdinalIgnoreCase);
        foreach (var chapter in feed)
        {
            var prefix = chapter.SourceChapterId.Split('-')[0].ToLowerInvariant();
            if (!index.TryGetValue(prefix, out var list))
            {
                index[prefix] = list = [];
            }

            list.Add(chapter);
        }

        return index;
    }

    private static MatchedItem Resolve(ScannedFile file, Dictionary<string, List<SourceChapter>> byPrefix)
    {
        if (file.UuidPrefix is null || !byPrefix.TryGetValue(file.UuidPrefix, out var candidates))
        {
            // Not in the current feed — purged (or the file predates any feed match). Disposition
            // is finalized by Dedup(); this stage only resolves the source-chapter identity.
            return new MatchedItem(file, null, null, MigrationItemDisposition.Pending, false, null);
        }

        if (candidates.Count > 1)
        {
            return new MatchedItem(file, null, null, MigrationItemDisposition.Unresolved, false,
                "This file's UUID prefix matches more than one chapter in the MangaDex feed.");
        }

        var chapter = candidates[0];
        var group = chapter.ScanlationGroups.Count > 0 ? chapter.ScanlationGroups[0] : null;
        // Carry MangaDex's own chapter number AND title alongside the local one — they can drift (a mod
        // renumbering the chapter on MangaDex after the old downloader grabbed it) even though the UUID
        // still matches. ApplyMatchAsync must key matched items off the feed's number+title so the
        // NumberKey agrees with the Chapter that ChapterImporter creates from the same feed at commit
        // (it keys by Normalize(number, title:) — the title matters for a numberless oneshot, whose key
        // is "title-<title>" not "oneshot"); otherwise commit fails with "matched release was not found
        // after importing the feed".
        return new MatchedItem(
            file, chapter.SourceChapterId, group, MigrationItemDisposition.Pending, false, null,
            chapter.Number, chapter.Title);
    }

    // --- Group ranking -------------------------------------------------------------------------

    private static List<string> RankGroups(IReadOnlyList<MatchedItem> items) =>
        items
            .Where(i => i.File.IntegrityFailureReason is null && i.MatchedGroup is not null)
            .GroupBy(i => i.MatchedGroup!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToList();

    private static int GroupRank(string? group, IReadOnlyList<string> ranking)
    {
        if (group is null)
        {
            return int.MaxValue;
        }

        for (var i = 0; i < ranking.Count; i++)
        {
            if (string.Equals(ranking[i], group, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    // --- Dedup per chapter number ---------------------------------------------------------------

    private static List<MatchedItem> Dedup(
        List<MatchedItem> items, IReadOnlyList<string> groupRanking, out bool hadHeuristicTies)
    {
        var result = new List<MatchedItem>();
        var anyHeuristicTie = false;

        foreach (var group in items.GroupBy(i => i.File.NumberKey))
        {
            var (quarantined, candidates) = Split(group);
            result.AddRange(quarantined.Select(i => i with
            {
                Disposition = MigrationItemDisposition.Quarantine,
                FlagReason = i.File.IntegrityFailureReason,
            }));

            var unresolved = candidates.Where(i => i.Disposition == MigrationItemDisposition.Unresolved).ToList();
            result.AddRange(unresolved);
            var eligible = candidates.Except(unresolved).ToList();
            if (eligible.Count == 0)
            {
                continue;
            }

            // Byte-identical duplicates (same source chapter, re-downloaded) collapse silently —
            // no guessing involved, so this never counts as a review-worthy tie.
            var representatives = eligible
                .GroupBy(i => i.File.UuidPrefix ?? i.File.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var kept = g.OrderBy(i => i.File.FileName, StringComparer.OrdinalIgnoreCase).First();
                    result.AddRange(g.Where(i => i != kept).Select(i => i with
                    {
                        Disposition = MigrationItemDisposition.Duplicate,
                        FlagReason = "Same source chapter as the kept copy.",
                    }));
                    return kept;
                })
                .ToList();

            if (representatives.Count == 1)
            {
                result.Add(representatives[0] with { Disposition = MigrationItemDisposition.Import, IsWinner = true });
                continue;
            }

            // Real tie: rank by recovered group first (trust the API over a guess); fall back to
            // "most pages, then no parenthetical suffix" when nothing has group data to rank by.
            var anyMatched = representatives.Any(i => i.MatchedSourceChapterId is not null);
            var winner = representatives
                .OrderBy(i => GroupRank(i.MatchedGroup, groupRanking))
                .ThenByDescending(i => i.File.PageCount)
                .ThenBy(i => HasParenthetical(i.File.ChapterTitle) ? 1 : 0)
                .ThenBy(i => i.File.FileName, StringComparer.OrdinalIgnoreCase)
                .First();

            if (!anyMatched)
            {
                anyHeuristicTie = true;
            }

            foreach (var candidate in representatives)
            {
                result.Add(candidate == winner
                    ? candidate with { Disposition = MigrationItemDisposition.Import, IsWinner = true }
                    : candidate with
                    {
                        Disposition = MigrationItemDisposition.Duplicate,
                        FlagReason = anyMatched
                            ? "Lower-ranked scanlation group for this chapter."
                            : "Best-guess pick went to another copy of this chapter (no group data available).",
                    });
            }
        }

        hadHeuristicTies = anyHeuristicTie;
        return result;
    }

    private static (List<MatchedItem> Quarantined, List<MatchedItem> Eligible) Split(IEnumerable<MatchedItem> items)
    {
        var quarantined = new List<MatchedItem>();
        var eligible = new List<MatchedItem>();
        foreach (var item in items)
        {
            (item.File.IntegrityFailureReason is null ? eligible : quarantined).Add(item);
        }

        return (quarantined, eligible);
    }

    private static bool HasParenthetical(string? title) => title is not null && title.Contains('(');
}
