using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MediaKind = MangaFusion.Contracts.Models.MediaKind;

namespace MangaFusion.UnitTests.Library;

public class MigrationMatcherTests
{
    private static ScannedFile File(
        string fileName, string? uuidPrefix, string? number, string? title = null,
        int pageCount = 20, string? integrityFailure = null) =>
        new(fileName, fileName, StorageFormat.Cbz, uuidPrefix, number,
            MangaFusion.Application.Library.ChapterNumber.Normalize(number).Key,
            title, null, pageCount, 500_000, integrityFailure);

    [Fact]
    public async Task Live_series_resolves_group_and_full_confidence()
    {
        // Mirrors the real New Princess Knight sample: local prefix matches the feed exactly, and
        // MangaDex is the only source of the scanlation group (ComicInfo never carries one).
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Shinyaku Ribbon no Kishi", AltTitles = ["New Princess Knight"] }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = "8161f066-aaaa-bbbb-cccc-000000000000", Number = "1", Language = "en", ScanlationGroups = ["Golden Roze"] }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("New Princess Knight", "/x", [File("Chapter1_..._8161f066.cbz", "8161f066", "1")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Equal(MigrationRegime.Live, result.Regime);
        Assert.Equal(1.0, result.Confidence);
        Assert.Null(result.ConflictReason);
        Assert.Equal(["Golden Roze"], result.GroupRanking);
        var item = Assert.Single(result.Items);
        Assert.Equal(MigrationItemDisposition.Import, item.Disposition);
        Assert.True(item.IsWinner);
        Assert.Equal("Golden Roze", item.MatchedGroup);
    }

    [Fact]
    public async Task Matched_oneshot_carries_the_feed_title_so_its_key_agrees_with_the_importer()
    {
        // Reproduces "#Project AC": a numberless oneshot whose MangaDex chapter has a title. ChapterImporter
        // keys chapters by Normalize(number, title:), so a titled oneshot's key is "title-<title>", not the
        // bare "oneshot". The matcher must carry the feed title through so ApplyMatchAsync persists the same
        // key — otherwise the imported release lands on a different chapter and commit fails with
        // "matched release ... was not found after importing the feed".
        const string uuid = "9ed789b1-d4f6-4277-a35f-f3ac9ba52914";
        const string feedTitle = "Oneshot";
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "#Project AC" }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = uuid, Number = null, Title = feedTitle, Language = "en", ScanlationGroups = ["Some Group"] }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("#Project AC", "/x",
            [File("Chapter-_[EN-data]_Oneshot_9ed789b1.cbz", "9ed789b1", null, title: "Oneshot")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(uuid, item.MatchedSourceChapterId);
        Assert.Null(item.ResolvedNumber);
        Assert.Equal(feedTitle, item.ResolvedTitle);

        // The key ApplyMatchAsync persists (from the resolved feed number+title) must equal the key
        // ChapterImporter creates from the same feed chapter — and it must be the titled key, not "oneshot".
        var migrationKey = MangaFusion.Application.Library.ChapterNumber.Normalize(item.ResolvedNumber, title: item.ResolvedTitle).Key;
        var importerKey = MangaFusion.Application.Library.ChapterNumber.Normalize(null, title: feedTitle).Key;
        Assert.Equal(importerKey, migrationKey);
        Assert.Equal("title-oneshot", migrationKey);
    }

    [Fact]
    public async Task Purged_series_still_commits_via_local_releases_when_title_matches_strongly()
    {
        // Mirrors Nagatoro: the feed has nothing for these chapters (licensed & delisted), but the
        // series title itself matches strongly, so it should resolve as Purged, not Unmatched.
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Ijiranaide, Nagatoro-san" }],
            Feed = [], // fully purged
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Ijiranaide, Nagatoro-san", "/x",
            [File("Chapter1_..._aaaaaaaa.cbz", "aaaaaaaa", "1"), File("Chapter2_..._bbbbbbbb.cbz", "bbbbbbbb", "2")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Equal(MigrationRegime.Purged, result.Regime);
        Assert.Equal("s1", result.MatchedSourceSeriesId);
        Assert.Null(result.ConflictReason);
        Assert.All(result.Items, i => Assert.Equal(MigrationItemDisposition.Import, i.Disposition));
    }

    [Fact]
    public async Task No_confident_title_match_is_unmatched_and_held_for_review()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Completely Unrelated Title" }],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("My Random Manga", "/x", [File("Chapter1_..._aaaaaaaa.cbz", "aaaaaaaa", "1")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Equal(MigrationRegime.Unmatched, result.Regime);
        Assert.NotNull(result.ConflictReason);
        Assert.All(result.Items, i => Assert.Equal(MigrationItemDisposition.Unresolved, i.Disposition));
    }

    [Fact]
    public async Task A_decoy_that_merely_contains_the_title_does_not_auto_accept()
    {
        // Real incident: "Sono Bisque Doll wa Koi o Suru" — an adult doujinshi spinoff whose alt-title
        // is "<real title> - My Sexy Dress-Up Darling" scored 0.75 (contains-match) under the old 0.6
        // bar and silently won, because the real series wasn't even in the search results returned.
        // A merely-plausible match must never auto-accept — it should fall to review instead.
        var source = new FakeMangaDexSource
        {
            SearchResults =
            [
                new SourceSeries
                {
                    SourceId = "mangadex", SourceSeriesId = "decoy",
                    Title = "Sono Bisque Doll wa Koi wo Suru - Sono Bisque Doll wa H o Suru",
                    AltTitles = ["Sono Bisque Doll wa Koi o Suru - My Sexy Dress-Up Darling"],
                },
            ],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder(
            "Sono Bisque Doll wa Koi o Suru", "/x", [File("Chapter1_..._aaaaaaaa.cbz", "aaaaaaaa", "1")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Equal(MigrationRegime.Unmatched, result.Regime);
        Assert.NotNull(result.ConflictReason);
    }

    [Fact]
    public async Task Exact_match_wins_over_a_higher_ranked_decoy()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults =
            [
                new SourceSeries
                {
                    SourceId = "mangadex", SourceSeriesId = "decoy",
                    Title = "Sono Bisque Doll wa Koi wo Suru - Sono Bisque Doll wa H o Suru",
                    AltTitles = ["Sono Bisque Doll wa Koi o Suru - My Sexy Dress-Up Darling"],
                },
                new SourceSeries
                {
                    SourceId = "mangadex", SourceSeriesId = "real",
                    Title = "Sono Bisque Doll wa Koi o Suru",
                },
            ],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder(
            "Sono Bisque Doll wa Koi o Suru", "/x", [File("Chapter1_..._aaaaaaaa.cbz", "aaaaaaaa", "1")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Equal("real", result.MatchedSourceSeriesId);
    }

    [Fact]
    public async Task Byte_identical_duplicate_prefix_collapses_silently()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        // Same UUID prefix twice — the exact same source chapter, redownloaded under two filenames.
        var folder = new ScannedSeriesFolder("Some Manga", "/x",
            [File("a_aaaaaaaa.cbz", "aaaaaaaa", "1"), File("b_aaaaaaaa.cbz", "aaaaaaaa", "1")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Null(result.ConflictReason); // no guessing involved — must not hold the series for review
        Assert.Single(result.Items, i => i.Disposition == MigrationItemDisposition.Import);
        Assert.Single(result.Items, i => i.Disposition == MigrationItemDisposition.Duplicate);
    }

    [Fact]
    public async Task Real_tie_with_no_group_data_uses_heuristic_and_flags_for_review()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [], // purged — no group data to break the tie with
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        // Two distinct chapter-1 copies (different prefixes) — a real tie the heuristic must resolve:
        // most pages wins, and a "(R18 Edit)" title is deprioritized against an equal-page plain one.
        var folder = new ScannedSeriesFolder("Some Manga", "/x",
        [
            File("a_aaaaaaaa.cbz", "aaaaaaaa", "1", pageCount: 40),
            File("b_bbbbbbbb.cbz", "bbbbbbbb", "1", pageCount: 20),
        ]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.NotNull(result.ConflictReason); // heuristic picks must be surfaced for review, not silent
        var winner = Assert.Single(result.Items, i => i.Disposition == MigrationItemDisposition.Import);
        Assert.Equal("a_aaaaaaaa.cbz", winner.File.FileName);
        Assert.True(winner.IsWinner);
    }

    [Fact]
    public async Task Quarantined_items_are_excluded_from_dedup_and_group_ranking()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = "aaaaaaaa-0000-0000-0000-000000000000", Number = "1", Language = "en", ScanlationGroups = ["Kodansha USA"] }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Some Manga", "/x",
            [File("a_aaaaaaaa.cbz", "aaaaaaaa", "1", pageCount: 0, integrityFailure: "no pages")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(MigrationItemDisposition.Quarantine, item.Disposition);
        Assert.Empty(result.GroupRanking); // the quarantined stub's group must not pollute preference ranking
    }

    [Fact]
    public async Task Series_starting_mid_run_with_no_chapter_1_anywhere_is_flagged()
    {
        // Mirrors "starts at chapter 20" — nothing local or on the feed covers the opener.
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Some Manga", "/x",
            [File("a_aaaaaaaa.cbz", "aaaaaaaa", "20"), File("b_bbbbbbbb.cbz", "bbbbbbbb", "21")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.NotNull(result.ConflictReason);
        Assert.Contains("chapter 1", result.ConflictReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chapter_1_only_as_an_external_MangaDex_stub_does_not_satisfy_the_opener()
    {
        // The real Nagatoro case: the local chapter-1 copy is pageless (quarantined), and MangaDex's
        // only chapter 1 is an external retail redirect — not something we can actually pull down.
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = "aaaaaaaa-0000-0000-0000-000000000000", Number = "1", Language = "en", IsExternal = true }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Some Manga", "/x",
        [
            File("a_aaaaaaaa.cbz", "aaaaaaaa", "1", pageCount: 0, integrityFailure: "no pages"),
            File("b_bbbbbbbb.cbz", "bbbbbbbb", "2"),
        ]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.NotNull(result.ConflictReason);
        Assert.Contains("chapter 1", result.ConflictReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Chapter_1_available_only_on_the_live_feed_satisfies_the_opener()
    {
        // No local file for chapter 1, but it's still a real, downloadable MangaDex release — the
        // user can fetch it in-app afterwards, so this must NOT be flagged.
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed =
            [
                new SourceChapter { SourceId = "mangadex", SourceChapterId = "aaaaaaaa-0000-0000-0000-000000000000", Number = "1", Language = "en" },
                new SourceChapter { SourceId = "mangadex", SourceChapterId = "bbbbbbbb-0000-0000-0000-000000000000", Number = "2", Language = "en" },
            ],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Some Manga", "/x", [File("b_bbbbbbbb.cbz", "bbbbbbbb", "2")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Null(result.ConflictReason);
    }

    [Fact]
    public async Task Oneshot_only_folder_has_no_opener_to_be_missing()
    {
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Some Manga" }],
            Feed = [],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Some Manga", "/x", [File("a_aaaaaaaa.cbz", "aaaaaaaa", number: null)]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        Assert.Null(result.ConflictReason);
    }

    [Fact]
    public async Task Matched_item_carries_the_feeds_own_chapter_number_when_it_differs_from_local()
    {
        // Regression for a real commit failure: MangaDex renumbered a chapter after the old
        // downloader grabbed it (same UUID prefix, but the feed's Number no longer matches the
        // local file's). ApplyMatchAsync must persist ResolvedNumber (not the local number) so the
        // MigrationItem's NumberKey agrees with the Chapter that ChapterImporter creates from the
        // same feed at commit time — otherwise FindOrCreateReleaseAsync can't find the release under
        // the chapter it just looked up and commit fails with "matched release was not found".
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Gyaru ni Kakomarete" }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = "aaaaaaaa-bbbb-cccc-dddd-000000000000", Number = "15", Language = "en", ScanlationGroups = ["Group"] }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        // Local file was downloaded when this chapter was still numbered "14".
        var folder = new ScannedSeriesFolder("Gyaru ni Kakomarete", "/x", [File("Chapter14_..._aaaaaaaa.cbz", "aaaaaaaa", "14")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(MigrationItemDisposition.Import, item.Disposition);
        Assert.Equal("15", item.ResolvedNumber);
    }

    [Fact]
    public async Task Matched_item_against_an_unnumbered_feed_chapter_resolves_number_as_null()
    {
        // Regression for a real commit failure: MangaDex reports `chapter: null` for some releases
        // (unnumbered "extra" chapters). The old downloader's scanner turns that into the literal
        // string "-" locally, but ChapterImporter normalizes a null feed number to the "oneshot" key
        // at commit time. ResolvedNumber must carry the feed's null through as-is (not the local
        // "-") so MigrationService.ApplyMatchAsync can normalize it the same way ChapterImporter
        // will, keeping the two in agreement.
        var source = new FakeMangaDexSource
        {
            SearchResults = [new SourceSeries { SourceId = "mangadex", SourceSeriesId = "s1", Title = "Gyaru ni Kakomareta" }],
            Feed = [new SourceChapter { SourceId = "mangadex", SourceChapterId = "2d5b9f6e-bbbb-cccc-dddd-000000000000", Number = null, Language = "en", ScanlationGroups = ["Schale scans"] }],
        };
        var matcher = new MigrationMatcher(new FakeSourceRegistry(source));
        var folder = new ScannedSeriesFolder("Gyaru ni Kakomareta", "/x", [File("Chapter-_..._2d5b9f6e.cbz", "2d5b9f6e", "-")]);

        var result = await matcher.MatchAsync(folder, CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(MigrationItemDisposition.Import, item.Disposition);
        Assert.Equal("2d5b9f6e-bbbb-cccc-dddd-000000000000", item.MatchedSourceChapterId);
        Assert.Null(item.ResolvedNumber);
    }
}

internal sealed class FakeMangaDexSource : ISource, IMetadataSource, IChapterSource
{
    public string Id => "mangadex";
    public string DisplayName => "MangaDex (fake)";
    public SourceCapabilities Capabilities => SourceCapabilities.Metadata | SourceCapabilities.Chapters;

    public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

    public List<SourceSeries> SearchResults { get; set; } = [];
    public List<SourceChapter> Feed { get; set; } = [];

    public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<SourceSeries>(SearchResults, SearchResults.Count, query.Limit, query.Offset));

    public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
        Task.FromResult(SearchResults.FirstOrDefault(s => s.SourceSeriesId == sourceSeriesId));

    public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceTag>>([]);

    public Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceSeriesId, ChapterQuery query, CancellationToken ct = default) =>
        Task.FromResult(new PagedResult<SourceChapter>(Feed, Feed.Count, query.Limit, query.Offset));
}

internal sealed class FakeSourceRegistry(FakeMangaDexSource source) : ISourceRegistry
{
    public IReadOnlyList<ISource> All => [source];

    public IReadOnlyList<ISource> ForKind(MangaFusion.Domain.Library.MediaKind kind) =>
        source.SupportedKinds.Contains(MediaKinds.ToContract(kind)) ? [source] : [];

    public bool Contains(string id) => id == source.Id;
    public ISource Get(string id) => source;
    public IMetadataSource GetMetadataSource(string id) => source;
    public IChapterSource GetChapterSource(string id) => source;
    public IDownloadSource GetDownloadSource(string id) => throw new NotSupportedException();
}
