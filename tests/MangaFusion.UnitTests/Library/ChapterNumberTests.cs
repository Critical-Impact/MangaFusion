using MangaFusion.Application.Library;

namespace MangaFusion.UnitTests.Library;

public class ChapterNumberTests
{
    [Theory]
    [InlineData("10", "10")]
    [InlineData("10.0", "10")]
    [InlineData("10.00", "10")]
    [InlineData("10.5", "10.5")]
    [InlineData(" 7 ", "7")]
    public void Normalize_produces_stable_key_for_equivalent_numbers(string input, string expectedKey) =>
        Assert.Equal(expectedKey, ChapterNumber.Normalize(input).Key);

    [Fact]
    public void Normalize_null_is_oneshot()
    {
        var (sort, key) = ChapterNumber.Normalize(null);
        Assert.Null(sort);
        Assert.Equal("oneshot", key);
    }

    [Fact]
    public void Normalize_parses_sort_value() =>
        Assert.Equal(10.5m, ChapterNumber.Normalize("10.5").Sort);

    [Fact]
    public void Normalize_blank_number_with_volume_keys_and_sorts_by_volume()
    {
        var (sort, key) = ChapterNumber.Normalize(null, "4");
        Assert.Equal(4m, sort);
        Assert.Equal("vol-4", key);
    }

    [Fact]
    public void Normalize_blank_number_with_different_volumes_do_not_collide()
    {
        Assert.NotEqual(
            ChapterNumber.Normalize(null, "1").Key,
            ChapterNumber.Normalize(null, "2").Key);
    }

    [Fact]
    public void Normalize_blank_number_and_blank_volume_is_still_oneshot()
    {
        var (sort, key) = ChapterNumber.Normalize(null, null);
        Assert.Null(sort);
        Assert.Equal("oneshot", key);
    }

    [Fact]
    public void Normalize_ignores_volume_when_number_is_present() =>
        Assert.Equal("10", ChapterNumber.Normalize("10", "4").Key);

    [Fact]
    public void Normalize_number_less_titles_do_not_collapse_onto_oneshot()
    {
        // A scraped "Prologue" and "Extra" (neither carries a recognizable number) must stay distinct.
        Assert.NotEqual(
            ChapterNumber.Normalize(null, title: "Prologue").Key,
            ChapterNumber.Normalize(null, title: "Extra").Key);
    }

    [Fact]
    public void Normalize_same_number_less_title_collapses_across_groups() =>
        Assert.Equal(
            ChapterNumber.Normalize(null, title: "Oneshot").Key,
            ChapterNumber.Normalize(null, title: " oneshot ").Key);

    [Fact]
    public void Normalize_ignores_title_when_number_is_present() =>
        Assert.Equal("10", ChapterNumber.Normalize("10", title: "Prologue").Key);

    [Fact]
    public void Normalize_ignores_title_when_volume_is_present() =>
        Assert.Equal("vol-4", ChapterNumber.Normalize(null, "4", "Prologue").Key);

    [Fact]
    public void Normalize_blank_title_is_still_oneshot() =>
        Assert.Equal("oneshot", ChapterNumber.Normalize(null, null, "   ").Key);

    [Fact]
    public void GroupKey_lowercases_primary_group() =>
        Assert.Equal("group a", ChapterNumber.GroupKey(["Group A", "Group B"]));

    [Fact]
    public void GroupKey_of_empty_is_null() =>
        Assert.Null(ChapterNumber.GroupKey([]));
}
