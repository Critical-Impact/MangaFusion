using MangaFusion.Infrastructure.Library;

namespace MangaFusion.UnitTests.Library;

/// <summary>The problem this solves: comics are relaunched endlessly under the same title, so a ComicVine
/// search for "Batman" returns a dozen volumes with identical names. Title similarity can't separate them —
/// the issue count can.</summary>
public class CandidateRankingTests
{
    /// <summary>The motivating case. A perfectly-titled 1-issue volume must lose to a decently-titled volume
    /// that's actually big enough to hold the 20 files being imported.</summary>
    [Fact]
    public void A_volume_too_small_to_hold_the_users_files_loses_to_a_plausible_one()
    {
        var perfectTitleButTiny = CandidateRanking.Score(titleScore: 1.0, candidateChapterCount: 1, localFileCount: 20);
        var decentTitleRightSize = CandidateRanking.Score(titleScore: 0.75, candidateChapterCount: 50, localFileCount: 20);

        Assert.True(
            decentTitleRightSize > perfectTitleButTiny,
            $"expected the plausibly-sized volume to win ({decentTitleRightSize} vs {perfectTitleButTiny})");
    }

    /// <summary>Asymmetry is the point: owning only part of a run is completely normal, so a candidate with
    /// far more issues than the user has files must not be penalised at all.</summary>
    [Fact]
    public void Having_more_issues_than_the_user_has_files_costs_nothing()
    {
        var exactlyEnough = CandidateRanking.Score(1.0, candidateChapterCount: 20, localFileCount: 20);
        var farMore = CandidateRanking.Score(1.0, candidateChapterCount: 500, localFileCount: 20);

        Assert.Equal(exactlyEnough, farMore);
        Assert.Equal(1.0, farMore);
    }

    /// <summary>The penalty scales with how impossible the candidate is, rather than being a cliff — a
    /// volume that's slightly too small is only slightly worse than one that's exactly right.</summary>
    [Fact]
    public void The_penalty_is_proportional_to_how_short_the_candidate_falls()
    {
        var slightlyShort = CandidateRanking.Score(1.0, candidateChapterCount: 18, localFileCount: 20);
        var wildlyShort = CandidateRanking.Score(1.0, candidateChapterCount: 2, localFileCount: 20);
        var exact = CandidateRanking.Score(1.0, candidateChapterCount: 20, localFileCount: 20);

        Assert.True(exact > slightlyShort);
        Assert.True(slightlyShort > wildlyShort);
    }

    /// <summary>Title stays the dominant signal: a plausible size can't rescue a candidate that simply isn't
    /// the series the user named.</summary>
    [Fact]
    public void A_plausible_issue_count_cannot_rescue_an_unrelated_title()
    {
        var rightSizeWrongName = CandidateRanking.Score(titleScore: 0.1, candidateChapterCount: 100, localFileCount: 20);
        var rightNameRightSize = CandidateRanking.Score(titleScore: 1.0, candidateChapterCount: 100, localFileCount: 20);

        Assert.True(rightNameRightSize > rightSizeWrongName);
    }

    /// <summary>No source is penalised for not reporting a count — MangaUpdates doesn't, and a manual search
    /// before any files are known passes 0. Both collapse to pure title similarity.</summary>
    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 20)]
    [InlineData(50, 0)]
    public void An_unknown_count_on_either_side_falls_back_to_the_title_score(int? candidateCount, int localFiles)
    {
        Assert.Equal(0.75, CandidateRanking.Score(0.75, candidateCount, localFiles));
    }
}
