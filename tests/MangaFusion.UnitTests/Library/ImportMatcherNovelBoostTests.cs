using MangaFusion.Contracts.Models;
using MangaFusion.Infrastructure.Library;
using MediaKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.UnitTests.Library;

/// <summary>MangaUpdates lists a light novel and its manga adaptation under near-identical titles,
/// tagging the novel with a "(Novel)"/"(Light Novel)" suffix. Since <see cref="TitleMatching"/> strips
/// punctuation, that suffix reads as an extra token that lowers the novel's title score against a
/// suffix-less folder name — so a small boost keeps it from losing to the manga adaptation when
/// importing into the light-novel library.</summary>
public class ImportMatcherNovelBoostTests
{
    private static SourceSeries Series(string title, params string[] altTitles) => new()
    {
        SourceId = "mangaupdates",
        SourceSeriesId = "1",
        Title = title,
        AltTitles = altTitles,
    };

    [Theory]
    [InlineData("Mushoku Tensei (Novel)")]
    [InlineData("Mushoku Tensei (Light Novel)")]
    [InlineData("mushoku tensei (novel)")] // case-insensitive
    public void A_novel_titled_entry_is_boosted_in_light_novel_mode(string title)
    {
        Assert.Equal(
            ImportMatcher.NovelTitleBoostAmount,
            ImportMatcher.NovelTitleBoost(MediaKind.LightNovel, Series(title)));
    }

    [Fact]
    public void The_suffix_is_also_matched_on_alt_titles()
    {
        Assert.Equal(
            ImportMatcher.NovelTitleBoostAmount,
            ImportMatcher.NovelTitleBoost(
                MediaKind.LightNovel, Series("Mushoku Tensei", "Jobless Reincarnation (Light Novel)")));
    }

    [Fact]
    public void A_plain_entry_gets_no_boost()
    {
        Assert.Equal(0, ImportMatcher.NovelTitleBoost(MediaKind.LightNovel, Series("Mushoku Tensei")));
    }

    [Fact]
    public void The_bare_word_novel_without_the_parenthesised_suffix_is_not_matched()
    {
        // Guards against false positives from "novel" appearing as an ordinary word in a real title.
        Assert.Equal(0, ImportMatcher.NovelTitleBoost(MediaKind.LightNovel, Series("The Novel Companion")));
    }

    [Theory]
    [InlineData(MediaKind.Manga)]
    [InlineData(MediaKind.Comic)]
    public void Only_the_light_novel_library_boosts_novel_entries(MediaKind kind)
    {
        Assert.Equal(0, ImportMatcher.NovelTitleBoost(kind, Series("Mushoku Tensei (Novel)")));
    }
}
