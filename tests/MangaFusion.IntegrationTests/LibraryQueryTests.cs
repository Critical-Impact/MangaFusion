using MangaFusion.Application.Library;
using MangaFusion.Application.Sources;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ContentRating = MangaFusion.Domain.Library.ContentRating;
using MediaKind = MangaFusion.Contracts.Models.MediaKind;
using DomainKind = MangaFusion.Domain.Library.MediaKind;

namespace MangaFusion.IntegrationTests;

public class LibraryQueryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"mf-libquery-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-libquery-lib-{Guid.NewGuid():N}");

    private AppDbContext NewContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options);

    private LibraryService NewService(AppDbContext db, ISourceRegistry registry = null!)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _root })
            .Build();
        var paths = new LibraryPaths(config);
        var authors = new AuthorResolver(db);
        var tagResolver = new TagResolver(db);
        return new LibraryService(
            db, registry, new ChapterImporter(db),
            new SeriesMetadataApplier(authors, tagResolver),
            new SeriesCoverCache(httpFactory: null!, paths, NullLogger<SeriesCoverCache>.Instance),
            tagResolver, paths);
    }

    private sealed class FakeMetadataSource(string id, IReadOnlyList<SourceTag> tags) : IMetadataSource
    {
        public string Id => id;
        public string DisplayName => id;
        public SourceCapabilities Capabilities => SourceCapabilities.Metadata;

        public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];
        public Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
            Task.FromResult(tags);
    }

    private sealed class FakeRegistry(IMetadataSource? source) : ISourceRegistry
    {
        public IReadOnlyList<ISource> All => source is null ? [] : [source];

        public IReadOnlyList<ISource> ForKind(MangaFusion.Domain.Library.MediaKind kind) =>
            source is not null && source.SupportedKinds.Contains(MediaKinds.ToContract(kind)) ? [source] : [];

        public bool Contains(string id) => source?.Id == id;
        public ISource Get(string id) => source?.Id == id ? source : throw new InvalidOperationException();
        public IMetadataSource GetMetadataSource(string id) =>
            source?.Id == id ? source : throw new InvalidOperationException();
        public IChapterSource GetChapterSource(string id) => throw new NotSupportedException();
        public IDownloadSource GetDownloadSource(string id) => throw new NotSupportedException();
    }

    private static Tag MakeTag(string name, string group = "genre") => new() { Name = name, Group = group };

    private static Series Seed(string title, int? year, DateTimeOffset addedAt, params Tag[] tags) => new()
    {
        Title = title,
        Year = year,
        AddedAt = addedAt,
        Tags = tags.ToList(),
    };

    private static readonly LibraryQuery DefaultQuery =
        new(DomainKind.Manga, null, [], null, "title", "asc", 24, 0);

    /// <summary>Backs the library grid's followed flag. The grid used to ask per row (a query per row, up to
    /// 100 a page); this answers for the whole page at once, so it has to be scoped to both the asking user
    /// and the requested page — not leak another user's follows, and not return series outside the page.</summary>
    [Fact]
    public async Task Followed_series_ids_are_scoped_to_the_user_and_the_requested_ids()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var followed = Seed("Berserk", 1989, DateTimeOffset.UtcNow);
        var unfollowed = Seed("Vinland Saga", 2005, DateTimeOffset.UtcNow);
        var offPage = Seed("Vagabond", 1998, DateTimeOffset.UtcNow);
        db.Series.AddRange(followed, unfollowed, offPage);

        var user = NewUser(db, "reader@test.local");
        var other = NewUser(db, "other@test.local");

        db.Follows.Add(new Follow { UserId = user, SeriesId = followed.Id });
        db.Follows.Add(new Follow { UserId = user, SeriesId = offPage.Id });   // followed, but not on this page
        db.Follows.Add(new Follow { UserId = other, SeriesId = unfollowed.Id }); // someone else's follow
        await db.SaveChangesAsync();

        var result = await NewService(db).GetFollowedSeriesIdsAsync(user, [followed.Id, unfollowed.Id]);

        Assert.Equal([followed.Id], result);
    }

    [Fact]
    public async Task Followed_series_ids_of_an_empty_page_is_empty()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        Assert.Empty(await NewService(db).GetFollowedSeriesIdsAsync(Guid.NewGuid(), []));
    }

    private static Guid NewUser(AppDbContext db, string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(user);
        return user.Id;
    }

    /// <summary>The whole point of the MediaKind axis: the two libraries share a database but must never
    /// see each other's series, no matter what else the query asks for.</summary>
    [Fact]
    public async Task Library_query_never_crosses_the_media_kind_boundary()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var manga = Seed("Berserk", 1989, DateTimeOffset.UtcNow);
        var comic = Seed("Batman", 1940, DateTimeOffset.UtcNow);
        comic.Kind = DomainKind.Comic;
        db.Series.AddRange(manga, comic);
        await db.SaveChangesAsync();

        var mangaPage = await NewService(db).QueryLibraryAsync(DefaultQuery);
        Assert.Equal(["Berserk"], mangaPage.Items.Select(i => i.Title));

        var comicPage = await NewService(db).QueryLibraryAsync(DefaultQuery with { Kind = DomainKind.Comic });
        Assert.Equal(["Batman"], comicPage.Items.Select(i => i.Title));
    }

    /// <summary>Tag rows are per-kind, so a comic's publisher/character facets must not appear in the
    /// manga browse filters (and vice versa) even though both live in the same table.</summary>
    [Fact]
    public async Task Tags_are_scoped_to_their_media_kind()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        var mangaGenre = MakeTag("Action");
        var comicPublisher = new Tag { Name = "DC Comics", Group = "publisher", Kind = DomainKind.Comic };
        var manga = Seed("Berserk", 1989, DateTimeOffset.UtcNow, mangaGenre);
        var comic = Seed("Batman", 1940, DateTimeOffset.UtcNow, comicPublisher);
        comic.Kind = DomainKind.Comic;
        db.Series.AddRange(manga, comic);
        await db.SaveChangesAsync();

        var mangaTags = await NewService(db).GetLibraryTagsAsync(DomainKind.Manga);
        Assert.Equal(["Action"], mangaTags.Select(t => t.Name));

        var comicTags = await NewService(db).GetLibraryTagsAsync(DomainKind.Comic);
        Assert.Equal(["DC Comics"], comicTags.Select(t => t.Name));
    }

    [Fact]
    public async Task Search_matches_title_case_insensitively()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Series.AddRange(
            Seed("One Piece", 1997, DateTimeOffset.UtcNow),
            Seed("One Punch Man", 2012, DateTimeOffset.UtcNow),
            Seed("Naruto", 1999, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery with { Search = "one p" });

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, i => Assert.StartsWith("One P", i.Title));
    }

    [Fact]
    public async Task Genre_filter_is_an_or_match()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var action = MakeTag("Action");
        var comedy = MakeTag("Comedy");
        var romance = MakeTag("Romance");
        db.Series.AddRange(
            Seed("A", 2000, DateTimeOffset.UtcNow, action, comedy),
            Seed("B", 2000, DateTimeOffset.UtcNow, romance),
            Seed("C", 2000, DateTimeOffset.UtcNow, comedy));
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery with { TagFacets = [[action.Id, romance.Id]] });

        Assert.Equal(2, page.Total);
        Assert.Equal(["A", "B"], page.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Genre_and_theme_facets_combine_with_and()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var actionGenre = MakeTag("Action", "genre");
        var schoolTheme = MakeTag("School Life", "theme");
        var comedyGenre = MakeTag("Comedy", "genre");
        db.Series.AddRange(
            Seed("Matches both", 2000, DateTimeOffset.UtcNow, actionGenre, schoolTheme),
            Seed("Genre only", 2000, DateTimeOffset.UtcNow, actionGenre),
            Seed("Theme only", 2000, DateTimeOffset.UtcNow, schoolTheme),
            Seed("Neither", 2000, DateTimeOffset.UtcNow, comedyGenre));
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(
            DefaultQuery with { TagFacets = [[actionGenre.Id], [schoolTheme.Id]] });

        Assert.Equal(["Matches both"], page.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Filters_by_content_rating()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var safe = Seed("Safe one", 2000, DateTimeOffset.UtcNow);
        safe.ContentRating = ContentRating.Safe;
        var suggestive = Seed("Suggestive one", 2000, DateTimeOffset.UtcNow);
        suggestive.ContentRating = ContentRating.Suggestive;
        db.Series.AddRange(safe, suggestive);
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery with { Rating = ContentRating.Safe });

        Assert.Equal(["Safe one"], page.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Sorts_by_year_descending()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Series.AddRange(
            Seed("Old", 1990, DateTimeOffset.UtcNow),
            Seed("New", 2020, DateTimeOffset.UtcNow),
            Seed("Mid", 2005, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery with { Sort = "year", Order = "desc" });

        Assert.Equal(["New", "Mid", "Old"], page.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Pages_results_and_reports_the_full_total()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        for (var i = 0; i < 5; i++)
        {
            db.Series.Add(Seed($"S{i}", 2000, DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery with { Limit = 2, Offset = 2 });

        Assert.Equal(5, page.Total);
        Assert.Equal(["S2", "S3"], page.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task Chapter_count_reflects_related_chapters()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var series = Seed("Has chapters", 2000, DateTimeOffset.UtcNow);
        series.Chapters.Add(new Chapter { Language = "en", NumberKey = "1" });
        series.Chapters.Add(new Chapter { Language = "en", NumberKey = "2" });
        db.Series.Add(series);
        await db.SaveChangesAsync();

        var page = await NewService(db).QueryLibraryAsync(DefaultQuery);

        Assert.Equal(2, Assert.Single(page.Items).ChapterCount);
    }

    [Fact]
    public async Task Library_tags_only_include_tags_in_use()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Series.Add(Seed("A", 2000, DateTimeOffset.UtcNow, MakeTag("Action")));
        db.Tags.Add(MakeTag("Unused")); // in the catalog, but not attached to any series
        await db.SaveChangesAsync();

        var inUse = await NewService(db).GetLibraryTagsAsync(DomainKind.Manga);
        Assert.Equal(["Action"], inUse.Select(t => t.Name));

        var catalog = await NewService(db).GetTagCatalogAsync(DomainKind.Manga);
        Assert.Equal(["Action", "Unused"], catalog.Select(t => t.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Library_tags_can_be_filtered_by_group()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        db.Series.Add(Seed("A", 2000, DateTimeOffset.UtcNow, MakeTag("Action", "genre"), MakeTag("School Life", "theme")));
        await db.SaveChangesAsync();

        var genres = await NewService(db).GetLibraryTagsAsync(DomainKind.Manga, "genre");
        Assert.Equal(["Action"], genres.Select(t => t.Name));
    }

    [Fact]
    public async Task Sync_upserts_a_sources_full_tag_registry()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var source = new FakeMetadataSource("mangadex",
            [new SourceTag("1", "Action", "genre"), new SourceTag("2", "Isekai", "genre")]);

        await NewService(db, new FakeRegistry(source)).SyncSourceTagsAsync("mangadex");

        var tags = await db.Tags.OrderBy(t => t.Name).ToListAsync();
        Assert.Equal(["Action", "Isekai"], tags.Select(t => t.Name));
        Assert.All(tags, t => Assert.Equal("mangadex", t.SourceId));
    }

    [Fact]
    public async Task Sync_is_idempotent()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();
        var source = new FakeMetadataSource("mangadex", [new SourceTag("1", "Action", "genre")]);
        var svc = NewService(db, new FakeRegistry(source));

        await svc.SyncSourceTagsAsync("mangadex");
        await svc.SyncSourceTagsAsync("mangadex");

        Assert.Single(await db.Tags.ToListAsync());
    }

    [Fact]
    public async Task Sync_no_ops_when_source_is_not_registered()
    {
        await using var db = NewContext();
        await db.Database.MigrateAsync();

        await NewService(db, new FakeRegistry(null)).SyncSourceTagsAsync("mangadex");

        Assert.Empty(await db.Tags.ToListAsync());
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        foreach (var f in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + "*"))
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }

        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
