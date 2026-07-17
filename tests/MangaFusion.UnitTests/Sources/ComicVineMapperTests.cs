using System.Text.Json;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.ComicVine.Dtos;
using MangaFusion.Sources.ComicVine.Mapping;

namespace MangaFusion.UnitTests.Sources;

/// <summary>Maps ComicVine's real response shapes onto the neutral contracts. Every fixture below is
/// shaped like a live response, not like the docs: a volume's credit lists are "people"/"characters"/
/// "concepts" (the *_credits names belong to the issue resource and silently return nothing on a volume),
/// people carry no role, `count` is a string, `aliases` is newline-separated or null, and `deck` is often a
/// useless stub next to a rich HTML `description`.</summary>
public class ComicVineMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static ComicVineVolumeDto Volume(string json) =>
        JsonSerializer.Deserialize<ComicVineVolumeDto>(json, JsonOptions)!;

    private static ComicVineIssueDto Issue(string json) =>
        JsonSerializer.Deserialize<ComicVineIssueDto>(json, JsonOptions)!;

    [Fact]
    public void Maps_a_volume_onto_a_source_series()
    {
        var series = ComicVineMapper.ToSeries(Volume(
            """
            {
              "id": 42721,
              "name": "Batman",
              "aliases": "The Dark Knight\nCaped Crusader",
              "deck": "Volume 2.",
              "description": "<p>A <i>long</i> HTML description.</p>",
              "start_year": "2011",
              "count_of_issues": 52,
              "site_detail_url": "https://comicvine.gamespot.com/batman/4050-42721/",
              "image": { "medium_url": "https://comicvine.gamespot.com/a/uploads/scale_medium/x.jpg" },
              "publisher": { "id": 10, "name": "DC Comics" },
              "people": [
                { "id": 2, "name": "Greg Capullo", "count": "40" },
                { "id": 1, "name": "Scott Snyder", "count": "52" }
              ],
              "characters": [
                { "id": 101, "name": "Joker", "count": "8" },
                { "id": 100, "name": "Batman", "count": "52" }
              ],
              "concepts": [ { "id": 300, "name": "Vigilante", "count": "12" } ]
            }
            """));

        Assert.Equal("comicvine", series.SourceId);
        Assert.Equal("42721", series.SourceSeriesId);
        Assert.Equal("Batman", series.Title);
        Assert.Equal(2011, series.Year);

        // count_of_issues is a real count, so it lands in ChapterCount — not LastChapter, which means a
        // last-issue *number*. The import matcher relies on it being a count to sanity-check candidates.
        Assert.Equal(52, series.ChapterCount);
        Assert.Null(series.LastChapter);

        // The site URL carries a slug that can't be reconstructed from the id, so it has to come from the
        // API rather than being built client-side.
        Assert.Equal("https://comicvine.gamespot.com/batman/4050-42721/", series.SiteUrl);
        Assert.Equal(["The Dark Knight", "Caped Crusader"], series.AltTitles);

        // The real prose wins: `deck` is frequently a stub like "Volume 2.".
        Assert.Equal("A long HTML description.", series.Description);

        // ComicVine has neither field, and guessing either would be wrong.
        Assert.Equal(ContentRating.Unknown, series.ContentRating);
        Assert.Equal(PublicationStatus.Unknown, series.Status);

        // A volume's people carry no role, so everyone is a "creator" — ranked by how many issues they
        // worked on. Artists is empty on purpose rather than guessed at.
        Assert.Equal(["Scott Snyder", "Greg Capullo"], series.Authors);
        Assert.Empty(series.Artists);

        // Publisher, characters and concepts stand in for a manga source's genre/theme tags. Characters
        // come back most-significant-first (Batman's 52 issues beat the Joker's 8), not in array order.
        Assert.Equal(
            [("publisher:10", "DC Comics", "publisher"),
             ("character:100", "Batman", "character"),
             ("character:101", "Joker", "character"),
             ("concept:300", "Vigilante", "concept")],
            series.TagRefs.Select(t => (t.Id, t.Name, t.Group)));
    }

    /// <summary>A ComicVine character and concept can share a numeric id, so the group has to be part of
    /// the persisted tag id or the two would collapse into one Tag row.</summary>
    [Fact]
    public void Tag_ids_are_namespaced_by_group_so_colliding_ids_stay_distinct()
    {
        var series = ComicVineMapper.ToSeries(Volume(
            """
            {
              "id": 1,
              "name": "V",
              "characters": [ { "id": 7, "name": "Rorschach", "count": "12" } ],
              "concepts": [ { "id": 7, "name": "Deconstruction", "count": "3" } ]
            }
            """));

        Assert.Equal(["character:7", "concept:7"], series.TagRefs.Select(t => t.Id));
    }

    [Fact]
    public void Strips_html_from_the_description()
    {
        var series = ComicVineMapper.ToSeries(Volume(
            """
            { "id": 1, "name": "V", "description": "<p>Hello   <b>world</b> &amp; friends.</p>" }
            """));

        Assert.Equal("Hello world & friends.", series.Description);
    }

    /// <summary>Real Sandman description: a leading &lt;figure&gt; whose caption ("House Ad") is editorial
    /// chrome. Stripping tags alone would make every such description open with the caption text.</summary>
    [Fact]
    public void Drops_figure_blocks_rather_than_leaking_their_captions_into_the_prose()
    {
        var series = ComicVineMapper.ToSeries(Volume(
            """
            {
              "id": 1, "name": "V",
              "description": "<figure><img src=\"x.jpg\"><figcaption>House Ad</figcaption></figure><p>The real prose.</p>"
            }
            """));

        Assert.Equal("The real prose.", series.Description);
    }

    [Fact]
    public void Falls_back_to_the_deck_when_there_is_no_description()
    {
        var series = ComicVineMapper.ToSeries(Volume("""{ "id": 1, "name": "V", "deck": "A short deck." }"""));
        Assert.Equal("A short deck.", series.Description);
    }

    /// <summary>The Sandman credits 196 characters, nearly all of them one-panel cameos. Keep the leads
    /// (highest issue count) and drop the tail, or the Tag table and the filter dropdown both drown.</summary>
    [Fact]
    public void Keeps_only_the_most_significant_characters_of_a_long_running_volume()
    {
        // Deliberately ascending, so array order and count order disagree.
        var characters = string.Join(',',
            Enumerable.Range(1, 200).Select(i => $$"""{ "id": {{i}}, "name": "Character {{i}}", "count": "{{i}}" }"""));

        var series = ComicVineMapper.ToSeries(Volume(
            $$"""{ "id": 1, "name": "V", "characters": [ {{characters}} ] }"""));

        var kept = series.TagRefs.Where(t => t.Group == "character").ToList();
        Assert.Equal(25, kept.Count);
        Assert.Equal("Character 200", kept[0].Name);   // the most-present character leads
        Assert.Equal("Character 176", kept[^1].Name);  // the cameo tail is dropped
    }

    /// <summary>A volume's `aliases` is null far more often than it's populated.</summary>
    [Fact]
    public void A_null_aliases_field_is_not_a_crash()
    {
        var series = ComicVineMapper.ToSeries(Volume("""{ "id": 1, "name": "V", "aliases": null }"""));
        Assert.Empty(series.AltTitles);
    }

    [Fact]
    public void A_blank_start_year_is_null_rather_than_zero()
    {
        var series = ComicVineMapper.ToSeries(Volume("""{ "id": 1, "name": "V", "start_year": "" }"""));
        Assert.Null(series.Year);
    }

    [Fact]
    public void Maps_an_issue_onto_a_source_chapter()
    {
        var chapter = ComicVineMapper.ToChapter(Issue(
            """
            {
              "id": 371980,
              "name": "Knife Trick",
              "issue_number": "1",
              "cover_date": "2011-11-01",
              "store_date": "2011-09-21"
            }
            """));

        Assert.Equal("comicvine", chapter.SourceId);
        Assert.Equal("371980", chapter.SourceChapterId);
        Assert.Equal("1", chapter.Number);
        Assert.Equal("Knife Trick", chapter.Title);
        Assert.Equal("en", chapter.Language);

        // store_date (the real on-sale date) wins over the months-ahead printed cover_date.
        Assert.Equal(new DateTimeOffset(2011, 9, 21, 0, 0, 0, TimeSpan.Zero), chapter.PublishedAt);

        // Comics have no scanlation groups — the group-preference machinery must find nothing to rank.
        Assert.Empty(chapter.ScanlationGroups);
    }

    [Fact]
    public void Falls_back_to_the_cover_date_when_there_is_no_store_date()
    {
        var chapter = ComicVineMapper.ToChapter(Issue(
            """{ "id": 1, "issue_number": "2", "cover_date": "1998-04-01" }"""));

        Assert.Equal(new DateTimeOffset(1998, 4, 1, 0, 0, 0, TimeSpan.Zero), chapter.PublishedAt);
    }

    /// <summary>Annuals and specials carry non-numeric issue numbers. They must survive as-is — the
    /// library's chapter-number normalizer already handles unparseable numbers by keying on the string.</summary>
    [Fact]
    public void A_non_numeric_issue_number_is_preserved()
    {
        var chapter = ComicVineMapper.ToChapter(Issue("""{ "id": 1, "issue_number": "Annual 1" }"""));

        Assert.Equal("Annual 1", chapter.Number);
        Assert.Null(chapter.Title);
    }
}
