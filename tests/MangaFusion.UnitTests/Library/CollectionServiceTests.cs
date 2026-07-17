using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Identity;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Library;

/// <summary>Collections are private per-user and kind-scoped. These tests pin the ownership boundary
/// (a user never sees or mutates another's collections), the kind guard on membership, idempotent
/// adds, manual reordering, and the preset sorts — the behaviour the endpoints lean on entirely.</summary>
public class CollectionServiceTests : IDisposable
{
    private readonly SqliteConnection _conn;
    private readonly AppDbContext _db;
    private readonly CollectionService _svc;
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"mf-colsvc-{Guid.NewGuid():N}");

    public CollectionServiceTests()
    {
        _conn = new SqliteConnection("Filename=:memory:");
        _conn.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        _db.Database.EnsureCreated();

        var paths = new LibraryPaths(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Library:RootPath"] = _base })
            .Build());
        var composer = new CollectionCoverComposer(paths, NullLogger<CollectionCoverComposer>.Instance);
        _svc = new CollectionService(_db, paths, composer);
    }

    public void Dispose()
    {
        _db.Dispose();
        _conn.Dispose();
        try { Directory.Delete(_base, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    private Guid AddUser()
    {
        var id = Guid.NewGuid();
        _db.Users.Add(new ApplicationUser { Id = id, UserName = id.ToString("N"), Email = $"{id:N}@x" });
        _db.SaveChanges();
        return id;
    }

    private Guid AddSeries(string title, MediaKind kind = MediaKind.Manga, int? year = null)
    {
        var series = new Series { Title = title, Kind = kind, Year = year };
        _db.Series.Add(series);
        _db.SaveChanges();
        return series.Id;
    }

    /// <summary>Gives a series a single chapter, optionally "downloaded" (with an active artifact) and
    /// optionally marked read (completed) by the user — the state the Unread dashboard filter keys on.</summary>
    private void AddChapter(Guid seriesId, bool downloaded, bool read = false, Guid? userId = null)
    {
        var chapter = new Chapter { SeriesId = seriesId, Language = "en", NumberKey = Guid.NewGuid().ToString("N") };
        if (downloaded)
        {
            var artifact = new Artifact { SeriesId = seriesId, Path = "x", Status = ArtifactStatus.Complete };
            _db.Artifacts.Add(artifact);
            chapter.ActiveArtifactId = artifact.Id;
        }
        _db.Chapters.Add(chapter);
        _db.SaveChanges();

        if (read && userId is { } uid)
        {
            _db.ReadingProgress.Add(new ReadingProgress { UserId = uid, ChapterId = chapter.Id, Completed = true });
            _db.SaveChanges();
        }
    }

    [Fact]
    public async Task Collections_are_scoped_to_the_owner_and_kind()
    {
        var alice = AddUser();
        var bob = AddUser();

        var mine = await _svc.CreateAsync(alice, MediaKind.Manga, "Mine", null);
        await _svc.CreateAsync(alice, MediaKind.Comic, "Comics", null); // other kind
        await _svc.CreateAsync(bob, MediaKind.Manga, "Bob's", null); // other user

        var listed = await _svc.GetCollectionsAsync(alice, MediaKind.Manga);

        Assert.Single(listed);
        Assert.Equal(mine.Id, listed[0].Id);
    }

    [Fact]
    public async Task Add_series_enforces_kind_and_is_idempotent()
    {
        var user = AddUser();
        var collection = await _svc.CreateAsync(user, MediaKind.Manga, "C", null);
        var manga = AddSeries("A Manga");
        var comic = AddSeries("A Comic", MediaKind.Comic);

        Assert.False(await _svc.AddSeriesAsync(user, collection.Id, comic)); // kind mismatch
        Assert.False(await _svc.AddSeriesAsync(user, collection.Id, Guid.NewGuid())); // missing series
        Assert.True(await _svc.AddSeriesAsync(user, collection.Id, manga));
        Assert.True(await _svc.AddSeriesAsync(user, collection.Id, manga)); // re-add is a no-op

        var detail = await _svc.GetCollectionAsync(user, collection.Id);
        Assert.NotNull(detail);
        Assert.Single(detail!.Members);
    }

    [Fact]
    public async Task Another_user_cannot_see_or_mutate_a_collection()
    {
        var owner = AddUser();
        var intruder = AddUser();
        var collection = await _svc.CreateAsync(owner, MediaKind.Manga, "Private", null);
        var series = AddSeries("S");

        Assert.Null(await _svc.GetCollectionAsync(intruder, collection.Id));
        Assert.False(await _svc.AddSeriesAsync(intruder, collection.Id, series));
        Assert.False(await _svc.UpdateAsync(intruder, collection.Id, "Hijacked", null, MemberSort.Manual, CollectionDashboardFilter.All));
        Assert.False(await _svc.DeleteAsync(intruder, collection.Id));

        // The owner's collection is untouched.
        var detail = await _svc.GetCollectionAsync(owner, collection.Id);
        Assert.Equal("Private", detail!.Name);
        Assert.Empty(detail.Members);
    }

    [Fact]
    public async Task Reorder_sets_manual_order()
    {
        var user = AddUser();
        var collection = await _svc.CreateAsync(user, MediaKind.Manga, "C", null);
        var s1 = AddSeries("One");
        var s2 = AddSeries("Two");
        var s3 = AddSeries("Three");
        await _svc.AddSeriesAsync(user, collection.Id, s1);
        await _svc.AddSeriesAsync(user, collection.Id, s2);
        await _svc.AddSeriesAsync(user, collection.Id, s3);

        Assert.True(await _svc.ReorderAsync(user, collection.Id, [s3, s1, s2]));

        var detail = await _svc.GetCollectionAsync(user, collection.Id);
        Assert.Equal(MemberSort.Manual, detail!.MemberSort);
        Assert.Equal([s3, s1, s2], detail.Members.Select(m => m.SeriesId).ToArray());
    }

    [Fact]
    public async Task Title_sort_orders_case_insensitively()
    {
        var user = AddUser();
        var collection = await _svc.CreateAsync(user, MediaKind.Manga, "C", null);
        await _svc.AddSeriesAsync(user, collection.Id, AddSeries("Beta"));
        await _svc.AddSeriesAsync(user, collection.Id, AddSeries("alpha"));

        await _svc.UpdateAsync(user, collection.Id, "C", null, MemberSort.TitleAsc, CollectionDashboardFilter.All);

        var detail = await _svc.GetCollectionAsync(user, collection.Id);
        Assert.Equal(["alpha", "Beta"], detail!.Members.Select(m => m.Title).ToArray());
    }

    [Fact]
    public async Task Membership_lists_collections_containing_a_series()
    {
        var user = AddUser();
        var series = AddSeries("S");
        var a = await _svc.CreateAsync(user, MediaKind.Manga, "A", null);
        var b = await _svc.CreateAsync(user, MediaKind.Manga, "B", null);
        await _svc.CreateAsync(user, MediaKind.Manga, "C", null); // series not added here
        await _svc.AddSeriesAsync(user, a.Id, series);
        await _svc.AddSeriesAsync(user, b.Id, series);

        var membership = await _svc.GetMembershipAsync(user, series);

        Assert.Equal(2, membership.Count);
        Assert.Contains(a.Id, membership);
        Assert.Contains(b.Id, membership);
    }

    [Fact]
    public async Task Unread_dashboard_filter_hides_read_and_undownloaded_members()
    {
        var user = AddUser();
        var unread = AddSeries("Unread");       // downloaded, not read → shows
        var read = AddSeries("Read");           // downloaded, read → hidden
        var undownloaded = AddSeries("Pending"); // not downloaded → hidden
        AddChapter(unread, downloaded: true);
        AddChapter(read, downloaded: true, read: true, userId: user);
        AddChapter(undownloaded, downloaded: false);

        var collection = await _svc.CreateAsync(user, MediaKind.Manga, "Shelf", null);
        foreach (var s in new[] { unread, read, undownloaded })
        {
            await _svc.AddSeriesAsync(user, collection.Id, s);
        }
        await _svc.UpdateAsync(user, collection.Id, "Shelf", null, MemberSort.TitleAsc, CollectionDashboardFilter.Unread);

        var onPage = await _svc.GetCollectionAsync(user, collection.Id, forDashboard: false);
        var onDashboard = await _svc.GetCollectionAsync(user, collection.Id, forDashboard: true);

        // The collection page always shows every member; the dashboard applies the Unread filter.
        Assert.Equal(3, onPage!.Members.Count);
        Assert.Equal([unread], onDashboard!.Members.Select(m => m.SeriesId).ToArray());
    }

    [Fact]
    public async Task Delete_removes_the_collection()
    {
        var user = AddUser();
        var collection = await _svc.CreateAsync(user, MediaKind.Manga, "Temp", null);

        Assert.True(await _svc.DeleteAsync(user, collection.Id));
        Assert.Null(await _svc.GetCollectionAsync(user, collection.Id));
    }
}
