using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Ranks metadata-source search candidates for one parsed group title. Pure decision logic (one
/// search call, no DB) — never auto-selects; the caller decides whether/how to use the ranked list
/// (the import wizard pre-fills the top candidate as a suggestion but always waits for the user to
/// confirm before committing).
///
/// Which source it searches follows the batch's library: manga is matched against MangaUpdates, comics
/// against ComicVine. Both are metadata-only — the files themselves always come from the local inbox.</summary>
public sealed class ImportMatcher(ISourceRegistry registry)
{
    private const int SearchLimit = 10;

    /// <summary>The metadata source a batch of the given kind is matched against.</summary>
    public static string SourceFor(MediaKind kind) => kind switch
    {
        MediaKind.Comic => "comicvine",
        _ => "mangaupdates",
    };

    /// <summary>Searches the kind's metadata source for <paramref name="title"/> and ranks the results, best
    /// first. Returns an empty list if nothing is found or the search fails.
    ///
    /// <paramref name="localFileCount"/> is how many files the user is importing for this series. It's used
    /// to sanity-check each candidate's issue count (see <see cref="CandidateRanking"/>) — the thing that
    /// separates the right "Batman" from the eleven other volumes also called "Batman". Pass 0 when unknown,
    /// which falls back to pure title similarity.</summary>
    public async Task<IReadOnlyList<SourceSeries>> SearchCandidatesAsync(
        MediaKind kind, string title, int localFileCount, CancellationToken ct)
    {
        var metadata = registry.GetMetadataSource(SourceFor(kind));
        var result = await metadata.SearchAsync(new SearchQuery { Text = title, Limit = SearchLimit }, ct);

        return result.Items
            .OrderByDescending(s => CandidateRanking.Score(
                TitleMatching.Score(title, [s.Title, .. s.AltTitles]),
                s.ChapterCount,
                localFileCount) + NovelTitleBoost(kind, s))
            .ToList();
    }

    /// <summary>A small ranking nudge toward a candidate MangaUpdates lists as a novel when the batch
    /// being matched imports into the light-novel library. MangaUpdates lists a light novel and its manga
    /// adaptation under near-identical titles, distinguishing the novel with a "(Novel)" (or "(Light
    /// Novel)") suffix — e.g. "Mushoku Tensei (Novel)". <see cref="TitleMatching.Score"/> strips
    /// punctuation, so that suffix reads as an extra "novel" token that actually <em>lowers</em> the
    /// novel entry's similarity against a suffix-less inbox folder name — without this it loses to the
    /// manga adaptation the user isn't importing. Deliberately small: it breaks near-ties toward the
    /// novel, not overrides a clearly better title match. Zero for every other library, so manga/comic
    /// ranking is unchanged.</summary>
    public const double NovelTitleBoostAmount = 0.15;

    public static double NovelTitleBoost(MediaKind kind, SourceSeries candidate) =>
        kind == MediaKind.LightNovel
        && (HasNovelSuffix(candidate.Title) || candidate.AltTitles.Any(HasNovelSuffix))
            ? NovelTitleBoostAmount
            : 0;

    private static bool HasNovelSuffix(string? title) =>
        title is not null
        && (title.Contains("(novel)", StringComparison.OrdinalIgnoreCase)
            || title.Contains("(light novel)", StringComparison.OrdinalIgnoreCase));

    /// <summary>Fetches one series by id — used when the user manually picks a match.</summary>
    public Task<SourceSeries?> GetSeriesAsync(MediaKind kind, string sourceSeriesId, CancellationToken ct) =>
        registry.GetMetadataSource(SourceFor(kind)).GetSeriesAsync(sourceSeriesId, ct);
}
