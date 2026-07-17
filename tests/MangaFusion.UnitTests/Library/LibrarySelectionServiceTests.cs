using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;

namespace MangaFusion.UnitTests.Library;

public class LibrarySelectionServiceTests
{
    private static ChapterRelease Rel(string? group, bool external = false, int daysAgo = 0) => new()
    {
        Id = Guid.NewGuid(),
        GroupKey = group,
        IsExternal = external,
        PublishedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo),
    };

    [Fact]
    public void SelectBest_prefers_group_order()
    {
        var best = LibrarySelectionService.SelectBest(
            [Rel("group b"), Rel("group a"), Rel("group c")], ["group a", "group b"]);
        Assert.Equal("group a", best!.GroupKey);
    }

    [Fact]
    public void SelectBest_falls_back_to_newest_unlisted()
    {
        var best = LibrarySelectionService.SelectBest([Rel("x", daysAgo: 5), Rel("y", daysAgo: 1)], []);
        Assert.Equal("y", best!.GroupKey);
    }

    [Fact]
    public void SelectBest_skips_external()
    {
        var best = LibrarySelectionService.SelectBest([Rel("a", external: true), Rel("b")], ["a"]);
        Assert.Equal("b", best!.GroupKey);
    }

    [Fact]
    public void SelectBest_null_when_all_external()
    {
        Assert.Null(LibrarySelectionService.SelectBest([Rel("a", external: true)], ["a"]));
    }

    [Fact]
    public void IsUpgrade_true_when_not_downloaded()
    {
        Assert.True(LibrarySelectionService.IsUpgrade(new Chapter(), Rel("a"), ["a"]));
    }

    [Fact]
    public void IsUpgrade_true_when_candidate_more_preferred()
    {
        var active = Rel("b");
        var candidate = Rel("a");
        var chapter = new Chapter { ActiveReleaseId = active.Id };
        chapter.Releases.Add(active);
        chapter.Releases.Add(candidate);

        Assert.True(LibrarySelectionService.IsUpgrade(chapter, candidate, ["a", "b"]));
    }

    [Fact]
    public void IsUpgrade_false_when_candidate_less_preferred()
    {
        var active = Rel("a");
        var candidate = Rel("b");
        var chapter = new Chapter { ActiveReleaseId = active.Id };
        chapter.Releases.Add(active);
        chapter.Releases.Add(candidate);

        Assert.False(LibrarySelectionService.IsUpgrade(chapter, candidate, ["a", "b"]));
    }
}
