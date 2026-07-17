namespace MangaFusion.Infrastructure.Library;

/// <summary>Scores an import-match candidate against what the user actually has on disk.
///
/// Title similarity alone is a weak signal for comics: publishers relaunch the same title endlessly, so a
/// ComicVine search for "Batman" returns a dozen volumes with identical names, differing only by start year
/// and issue count. The issue count is the discriminator we can actually check — if the user is importing 20
/// files and a candidate volume only ever had 1 issue, it is not that series, however perfectly the title
/// matches.
///
/// The rule is deliberately <b>asymmetric</b>. A candidate with *more* issues than the user has is entirely
/// normal — a partial collection is the common case — so it costs nothing. A candidate with *fewer* issues
/// than the user has files is close to impossible, and is penalised in proportion to how impossible it
/// is.</summary>
public static class CandidateRanking
{
    /// <summary>How much of the final score title similarity is worth, with the rest coming from the
    /// issue-count sanity check. Title stays dominant: the count is a tiebreaker and a filter for the
    /// absurd, not a substitute for actually matching the name.</summary>
    private const double TitleWeight = 0.65;

    /// <summary>Combined score in [0,1]. <paramref name="titleScore"/> is <see cref="TitleMatching.Score"/>'s
    /// output. <paramref name="candidateChapterCount"/> is what the source says the series has;
    /// <paramref name="localFileCount"/> is how many files the user is importing. Either being unknown (null
    /// or zero) collapses this to the title score alone — no source is penalised for not reporting a count.</summary>
    public static double Score(double titleScore, int? candidateChapterCount, int localFileCount)
    {
        if (candidateChapterCount is not > 0 || localFileCount <= 0)
        {
            return titleScore;
        }

        return (titleScore * TitleWeight) + (PlausibilityOfCount(candidateChapterCount.Value, localFileCount) * (1 - TitleWeight));
    }

    /// <summary>1.0 when the candidate is big enough to hold everything the user has; otherwise the fraction
    /// of the user's files it could actually account for. 20 local files against a 1-issue volume scores
    /// 0.05 — enough to sink it below a worse-titled candidate that's at least the right size.</summary>
    private static double PlausibilityOfCount(int candidateChapterCount, int localFileCount) =>
        candidateChapterCount >= localFileCount
            ? 1.0
            : (double)candidateChapterCount / localFileCount;
}
