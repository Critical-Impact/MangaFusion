using MangaFusion.Infrastructure.Library;

namespace MangaFusion.UnitTests.Library;

public class ImportScannerTests
{
    // Real sample release names (Yen Press digital volume dumps) — verified against the live
    // MangaUpdates API that the two "Explosion" volumes are the same series and "God's Blessing"
    // vol. 1 is a genuinely different one, so the parser must produce the same title for the first
    // two and a different one for the third.
    [Theory]
    [InlineData(
        "Yen.Press-Konosuba.An.Explosion.On.This.Wonderful.World.Vol.03.Manga.2022.Hybrid.Comic.eBook-BitBook",
        "Konosuba An Explosion On This Wonderful World", "3")]
    [InlineData(
        "Yen.Press-Konosuba.An.Explosion.On.This.Wonderful.World.Vol.04.Manga.2022.Hybrid.Comic.eBook-BitBook",
        "Konosuba An Explosion On This Wonderful World", "4")]
    [InlineData(
        "Yen.Press-Konosuba.God.s.Blessing.On.This.Wonderful.World.Vol.01.Manga.2022.Hybrid.Comic.eBook-BitBook",
        "Konosuba God s Blessing On This Wonderful World", "1")]
    public void ParseFolderName_strips_publisher_group_and_noise_tokens(
        string folderName, string expectedTitle, string expectedVolume)
    {
        var (title, volume, _) = ImportScanner.ParseFolderName(folderName);
        Assert.Equal(expectedTitle, title);
        Assert.Equal(expectedVolume, volume);
    }

    [Fact]
    public void ParseFolderName_falls_back_to_the_raw_name_when_nothing_survives()
    {
        var (title, volume, _) = ImportScanner.ParseFolderName("Manga.eBook.Hybrid");
        Assert.Equal("Manga.eBook.Hybrid", title);
        Assert.Null(volume);
    }

    [Fact]
    public void ParseFolderName_handles_no_publisher_or_group_tags()
    {
        var (title, volume, _) = ImportScanner.ParseFolderName("Some.Series.Vol.02");
        Assert.Equal("Some Series", title);
        Assert.Equal("2", volume);
    }

    [Theory]
    [InlineData("Some Series v03", "3")]
    [InlineData("Some Series V3", "3")]
    [InlineData("Some Series Volume 3", "3")]
    [InlineData("Some Series vol.3", "3")]
    public void ParseFolderName_recognizes_short_v_prefixed_volume_markers(string folderName, string expectedVolume)
    {
        var (_, volume, _) = ImportScanner.ParseFolderName(folderName);
        Assert.Equal(expectedVolume, volume);
    }

    /// <summary>Comics ship one file per <em>issue</em>, not per volume. "#017" is the unambiguous marker
    /// and is trusted anywhere; a bare number is only trusted at the end of the name, because a number
    /// anywhere else is usually part of the title — "100 Bullets" is a series, not issue 100 of "Bullets".</summary>
    [Theory]
    [InlineData("100 Bullets #017", "17")]
    [InlineData("100 Bullets #17", "17")]
    [InlineData("Batman #1", "1")]
    [InlineData("Watchmen #012 (of 12)", "12")]
    [InlineData("Saga #0", "0")]
    [InlineData("Astro City #1.5", "1.5")]
    // Trailing bare numbers, with the junk comic filenames actually carry.
    [InlineData("100 Bullets 017", "17")]
    [InlineData("Batman 001 (2000)", "1")]
    [InlineData("Preacher 015 (1996) (digital) (Minutemen-Slayer)", "15")]
    public void ParseIssue_reads_the_issue_number(string fileName, string expected) =>
        Assert.Equal(expected, ImportScanner.ParseIssue(fileName));

    /// <summary>The dangerous cases. Guessing wrong here is worse than not guessing: a title that merely
    /// starts with a number, a collected edition (a volume, not an issue), and a bare publication year.</summary>
    [Theory]
    [InlineData("100 Bullets")]          // the number is the title, not an issue
    [InlineData("Saga v01")]             // a trade paperback: a volume, not an issue
    [InlineData("Fables Vol. 3")]
    [InlineData("Batman (2016)")]        // a year, not issue 2016
    [InlineData("Preacher")]
    public void ParseIssue_declines_to_guess_when_the_number_is_not_an_issue(string fileName) =>
        Assert.Null(ImportScanner.ParseIssue(fileName));

    /// <summary>A folder-per-issue layout must still group into one series — otherwise every issue of
    /// "100 Bullets" becomes its own one-file series to match separately.</summary>
    [Fact]
    public void ParseFolderName_strips_the_issue_marker_from_the_title()
    {
        var (title, _, issue) = ImportScanner.ParseFolderName("100 Bullets #017");

        Assert.Equal("100 Bullets", title);
        Assert.Equal("17", issue);
    }
}
