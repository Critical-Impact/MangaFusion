using MangaFusion.Contracts.Models;
using MangaFusion.Sources.MangaDex.Dtos;
using MangaFusion.Sources.MangaDex.Mapping;

namespace MangaFusion.UnitTests.MangaDex;

public class MangaDexMapperTests
{
    [Fact]
    public void ToSeries_maps_core_fields_and_prefers_english_title()
    {
        var dto = new MangaDataDto
        {
            Id = "abc",
            Attributes = new MangaAttributesDto
            {
                Title = new() { ["ja"] = "ドロヘドロ", ["en"] = "Dorohedoro" },
                Description = new() { ["en"] = "desc" },
                Status = "completed",
                ContentRating = "suggestive",
                Year = 2000,
                OriginalLanguage = "ja",
                AvailableTranslatedLanguages = ["en", "fr"],
                Tags = [new TagDto { Attributes = new TagAttributesDto { Name = new() { ["en"] = "Comedy" } } }],
            },
            Relationships =
            [
                new RelationshipDto { Type = "cover_art", Attributes = new RelationshipAttributesDto { FileName = "cover.jpg" } },
                new RelationshipDto { Type = "author", Attributes = new RelationshipAttributesDto { Name = "Q Hayashida" } },
            ],
        };

        var series = MangaDexMapper.ToSeries(dto);

        Assert.Equal("mangadex", series.SourceId);
        Assert.Equal("abc", series.SourceSeriesId);
        Assert.Equal("Dorohedoro", series.Title);
        Assert.Equal(ContentRating.Suggestive, series.ContentRating);
        Assert.Equal(PublicationStatus.Completed, series.Status);
        Assert.Equal(2000, series.Year);
        Assert.Contains("Q Hayashida", series.Authors);
        Assert.Contains("Comedy", series.Tags);
        Assert.Equal("https://uploads.mangadex.org/covers/abc/cover.jpg.512.jpg", series.CoverUrl);
        Assert.Equal(new[] { "en", "fr" }, series.AvailableTranslatedLanguages);
    }

    [Fact]
    public void ToSeries_falls_back_to_first_title_when_no_english()
    {
        var dto = new MangaDataDto
        {
            Id = "x",
            Attributes = new MangaAttributesDto { Title = new() { ["ja"] = "タイトル" } },
        };

        Assert.Equal("タイトル", MangaDexMapper.ToSeries(dto).Title);
    }

    [Fact]
    public void ToChapter_detects_external_and_maps_group()
    {
        var dto = new ChapterDataDto
        {
            Id = "ch1",
            Attributes = new ChapterAttributesDto
            {
                Chapter = "10.5",
                Volume = "2",
                TranslatedLanguage = "en",
                ExternalUrl = "https://example/read",
                Pages = 0,
            },
            Relationships =
            [
                new RelationshipDto { Type = "scanlation_group", Attributes = new RelationshipAttributesDto { Name = "Group X" } },
            ],
        };

        var chapter = MangaDexMapper.ToChapter(dto);

        Assert.Equal("10.5", chapter.Number);
        Assert.Equal("2", chapter.Volume);
        Assert.Equal("en", chapter.Language);
        Assert.True(chapter.IsExternal);
        Assert.Contains("Group X", chapter.ScanlationGroups);
    }
}
