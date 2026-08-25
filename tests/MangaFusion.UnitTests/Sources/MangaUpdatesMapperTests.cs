using System.Text.Json;
using MangaFusion.Sources.MangaUpdates.Dtos;
using MangaFusion.Sources.MangaUpdates.Mapping;

namespace MangaFusion.UnitTests.Sources;

/// <summary>These tests map the real GET /series/{id} response onto the neutral contracts. Each fixture
/// is a live response with unused fields removed. Note these shapes: "year" is a string; "type" gives
/// both the comic family and the light-novel routing; authors and artists share one list, keyed by
/// "type". An author_id can also be null. That last condition stopped the full import.</summary>
public class MangaUpdatesMapperTests
{
    // The same options as MangaUpdatesApiClient. The API uses snake_case.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private static SeriesModelDto Series(string json) =>
        JsonSerializer.Deserialize<SeriesModelDto>(json, JsonOptions)!;

    /// <summary>Solo Leveling, with unused fields removed. The series credits "DISCIPLES (Redice
    /// Studio)", but MangaUpdates has no author record for it. Its author_id and url are null.</summary>
    private const string SoloLevelingJson =
        """
        {
          "series_id": 15180124327,
          "title": "Solo Leveling",
          "url": "https://www.mangaupdates.com/series/6z1uqw7/solo-leveling",
          "associated": [ { "title": "Na Honjaman Level-Up" } ],
          "description": "Known as the Weakest Hunter of All Mankind...",
          "image": { "url": { "original": "https://www.mangaupdates.com/image/i123.jpg", "thumb": null } },
          "type": "Manhwa",
          "year": "2018",
          "genres": [ { "genre": "Action" }, { "genre": "Fantasy" } ],
          "status": "200 Chapters + Prologue (Complete)",
          "completed": true,
          "authors": [
            { "name": "Chugong", "author_id": 71651794005, "url": "https://www.mangaupdates.com/author/wwzmm39/chugong", "type": "Author" },
            { "name": "DISCIPLES (Redice Studio)", "author_id": null, "url": null, "type": "Artist" },
            { "name": "DUBU (Redice Studio)", "author_id": 61564873920, "url": "https://www.mangaupdates.com/author/sa64wqo/dubu", "type": "Artist" }
          ]
        }
        """;

    [Fact]
    public void Maps_a_series_onto_a_source_series()
    {
        var series = MangaUpdatesMapper.ToSeries(Series(SoloLevelingJson));

        Assert.Equal("mangaupdates", series.SourceId);
        Assert.Equal("15180124327", series.SourceSeriesId);
        Assert.Equal("Solo Leveling", series.Title);
        Assert.Equal(["Na Honjaman Level-Up"], series.AltTitles);
        Assert.Equal(2018, series.Year);
        Assert.Equal(MangaFusion.Contracts.Models.MediaKind.Manga, series.Kind);
        Assert.Equal("ko", series.OriginalLanguage);
        Assert.Equal(MangaFusion.Contracts.Models.PublicationStatus.Completed, series.Status);
        Assert.Equal(["Action", "Fantasy"], series.Tags);

        // The "type" field divides one list into the two credit roles.
        Assert.Equal(["Chugong"], series.Authors);
        Assert.Equal(["DISCIPLES (Redice Studio)", "DUBU (Redice Studio)"], series.Artists);
    }

    [Fact]
    public void Keeps_a_credit_whose_author_id_is_null()
    {
        var series = MangaUpdatesMapper.ToSeries(Series(SoloLevelingJson));

        // The credit with no id stays in the list. It keeps its position and has a null ref id.
        // AuthorResolver then finds the author by name.
        Assert.Equal(2, series.ArtistRefs.Count);
        Assert.Null(series.ArtistRefs[0].Id);
        Assert.Equal("DISCIPLES (Redice Studio)", series.ArtistRefs[0].Name);
        Assert.Equal("61564873920", series.ArtistRefs[1].Id);

        Assert.Equal("71651794005", Assert.Single(series.AuthorRefs).Id);
    }

    [Fact]
    public void Routes_a_novel_to_the_light_novel_library()
    {
        var series = MangaUpdatesMapper.ToSeries(Series(
            """
            { "series_id": 13184758110, "title": "Solo Leveling (Novel)", "type": "Novel", "year": "2016", "completed": true }
            """));

        Assert.Equal(MangaFusion.Contracts.Models.MediaKind.LightNovel, series.Kind);
        Assert.Null(series.OriginalLanguage);
    }
}
