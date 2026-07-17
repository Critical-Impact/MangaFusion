using MangaFusion.Application.Monitoring;
using MangaFusion.Domain.Library;

namespace MangaFusion.UnitTests.Monitoring;

public class AutoDownloadPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 0, 0, 0, TimeSpan.Zero);

    private static ChapterRelease Rel(string? group, bool external = false, int publishedDaysAgo = 0)
    {
        var published = Now.AddDays(-publishedDaysAgo);
        return new ChapterRelease
        {
            Id = Guid.NewGuid(),
            GroupKey = group,
            IsExternal = external,
            PublishedAt = published,
            DiscoveredAt = published,
        };
    }

    private static Chapter Chapter(string language, bool downloaded, params ChapterRelease[] releases)
    {
        var c = new Chapter { Id = Guid.NewGuid(), Language = language };
        c.Releases.AddRange(releases);
        if (downloaded)
        {
            c.ActiveArtifactId = Guid.NewGuid();
            c.ActiveReleaseId = releases[0].Id; // pretend the first release is active
        }

        return c;
    }

    private static Series Series(IEnumerable<string> preferred, params Chapter[] chapters)
    {
        var s = new Series { PreferredGroups = preferred.ToList() };
        s.Chapters.AddRange(chapters);
        return s;
    }

    private static IReadOnlyList<MonitorDecision> Plan(Series s, int graceDays = 7, ISet<Guid>? pending = null) =>
        AutoDownloadPlanner.Plan(s, ["en"], graceDays, Now, pending ?? new HashSet<Guid>());

    [Fact]
    public void Preferred_available_downloads_now()
    {
        var chapter = Chapter("en", downloaded: false, Rel("group b", publishedDaysAgo: 0), Rel("group a", publishedDaysAgo: 0));
        var decisions = Plan(Series(["group a"], chapter));

        var d = Assert.Single(decisions);
        Assert.Equal(DecisionKind.Download, d.Kind);
    }

    [Fact]
    public void No_preferences_downloads_now()
    {
        var chapter = Chapter("en", downloaded: false, Rel("x"));
        var decisions = Plan(Series([], chapter));
        Assert.Equal(DecisionKind.Download, Assert.Single(decisions).Kind);
    }

    [Fact]
    public void Only_non_preferred_within_grace_defers()
    {
        // Preferences exist, but only an unlisted group released 2 days ago; grace is 7 days.
        var chapter = Chapter("en", downloaded: false, Rel("unlisted", publishedDaysAgo: 2));
        Assert.Empty(Plan(Series(["preferred"], chapter)));
    }

    [Fact]
    public void Only_non_preferred_after_grace_downloads()
    {
        var chapter = Chapter("en", downloaded: false, Rel("unlisted", publishedDaysAgo: 10));
        Assert.Equal(DecisionKind.Download, Assert.Single(Plan(Series(["preferred"], chapter))).Kind);
    }

    [Fact]
    public void Downloaded_with_more_preferred_release_replaces()
    {
        var active = Rel("group b");
        var better = Rel("group a");
        var chapter = Chapter("en", downloaded: true, active, better); // active = first (group b)
        var decisions = Plan(Series(["group a", "group b"], chapter));

        var d = Assert.Single(decisions);
        Assert.Equal(DecisionKind.Replace, d.Kind);
        Assert.Equal(better.Id, d.ReleaseId);
    }

    [Fact]
    public void Downloaded_on_best_group_does_nothing()
    {
        var active = Rel("group a");
        var worse = Rel("group b");
        var chapter = Chapter("en", downloaded: true, active, worse); // active = first (group a)
        Assert.Empty(Plan(Series(["group a", "group b"], chapter)));
    }

    [Fact]
    public void Skips_unwanted_language()
    {
        var chapter = Chapter("fr", downloaded: false, Rel("x"));
        Assert.Empty(Plan(Series([], chapter))); // wantedLanguages = ["en"]
    }

    [Fact]
    public void Skips_chapter_with_pending_download()
    {
        var chapter = Chapter("en", downloaded: false, Rel("x"));
        Assert.Empty(Plan(Series([], chapter), pending: new HashSet<Guid> { chapter.Id }));
    }

    [Fact]
    public void Skips_external_only_chapter()
    {
        var chapter = Chapter("en", downloaded: false, Rel("official", external: true));
        Assert.Empty(Plan(Series([], chapter)));
    }

    [Fact]
    public void Empty_wanted_languages_downloads_nothing()
    {
        var chapter = Chapter("en", downloaded: false, Rel("x"));
        Assert.Empty(AutoDownloadPlanner.Plan(Series([], chapter), [], 7, Now, new HashSet<Guid>()));
    }
}
