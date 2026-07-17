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
                localFileCount))
            .ToList();
    }

    /// <summary>Fetches one series by id — used when the user manually picks a match.</summary>
    public Task<SourceSeries?> GetSeriesAsync(MediaKind kind, string sourceSeriesId, CancellationToken ct) =>
        registry.GetMetadataSource(SourceFor(kind)).GetSeriesAsync(sourceSeriesId, ct);
}
