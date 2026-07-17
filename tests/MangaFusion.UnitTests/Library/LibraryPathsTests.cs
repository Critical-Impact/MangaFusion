using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.UnitTests.Library;

/// <summary>Manga and comics live under separate roots so they can sit on different volumes. The
/// consequence is that a stored relative path (<c>Artifact.Path</c>, <c>Series.CoverPath</c>) is only
/// meaningful alongside its kind — resolving one against the wrong root silently points at a file that
/// isn't there, or worse, a different series' file with the same name.</summary>
public class LibraryPathsTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"mf-paths-{Guid.NewGuid():N}");

    private LibraryPaths Build(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?> { ["Library:RootPath"] = _base };
        foreach (var (k, v) in overrides ?? [])
        {
            settings[k] = v;
        }

        return new LibraryPaths(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    [Fact]
    public void The_two_roots_default_to_siblings_under_the_base_root()
    {
        var paths = Build();

        Assert.Equal(Path.Combine(_base, "manga"), paths.Root(MediaKind.Manga));
        Assert.Equal(Path.Combine(_base, "comics"), paths.Root(MediaKind.Comic));
        Assert.True(Directory.Exists(paths.Root(MediaKind.Manga)));
        Assert.True(Directory.Exists(paths.Root(MediaKind.Comic)));
    }

    /// <summary>The whole point of separate roots: a self-hoster can put comics on a NAS and manga on an
    /// SSD, with no shared parent.</summary>
    [Fact]
    public void Each_root_can_be_overridden_to_an_unrelated_location()
    {
        var comicRoot = Path.Combine(Path.GetTempPath(), $"mf-comics-{Guid.NewGuid():N}");
        var paths = Build(new Dictionary<string, string?> { ["Library:ComicRootPath"] = comicRoot });

        Assert.Equal(Path.GetFullPath(comicRoot), paths.Root(MediaKind.Comic));
        Assert.Equal(Path.Combine(_base, "manga"), paths.Root(MediaKind.Manga));

        Directory.Delete(comicRoot, recursive: true);
    }

    /// <summary>The same relative path resolves to two different files depending on its kind — which is
    /// exactly why every resolve site has to be handed one.</summary>
    [Fact]
    public void A_relative_path_resolves_against_its_own_kinds_root()
    {
        var paths = Build();
        const string relative = "Berserk/Berserk - Ch. 1.cbz";

        var manga = paths.Absolute(MediaKind.Manga, relative);
        var comic = paths.Absolute(MediaKind.Comic, relative);

        Assert.NotEqual(manga, comic);
        Assert.StartsWith(paths.Root(MediaKind.Manga), manga, StringComparison.Ordinal);
        Assert.StartsWith(paths.Root(MediaKind.Comic), comic, StringComparison.Ordinal);
    }

    [Fact]
    public void Absolute_and_RelativeTo_round_trip()
    {
        var paths = Build();
        var absolute = Path.Combine(paths.SeriesDirectory(MediaKind.Comic, "The Sandman"), "001.cbz");

        var relative = paths.RelativeTo(MediaKind.Comic, absolute);

        Assert.Equal("The Sandman/001.cbz".Replace('/', Path.DirectorySeparatorChar), relative);
        Assert.Equal(absolute, paths.Absolute(MediaKind.Comic, relative));
    }

    /// <summary>The traversal guard is deliberately "under <em>any</em> root" — it answers "does this
    /// escape the library", not "is this the right library". A comic artifact reaching the reader must not
    /// be rejected just because it isn't under the manga root.</summary>
    [Fact]
    public void IsUnderAnyRoot_accepts_both_libraries_and_rejects_escapes()
    {
        var paths = Build();

        Assert.True(paths.IsUnderAnyRoot(paths.Absolute(MediaKind.Manga, "Berserk/1.cbz")));
        Assert.True(paths.IsUnderAnyRoot(paths.Absolute(MediaKind.Comic, "Sandman/1.cbz")));
        Assert.True(paths.IsUnderAnyRoot(paths.Root(MediaKind.Comic)));

        Assert.False(paths.IsUnderAnyRoot(Path.Combine(_base, "not-a-library", "x.cbz")));
        Assert.False(paths.IsUnderAnyRoot(paths.Absolute(MediaKind.Manga, "../../../etc/passwd")));
        Assert.False(paths.IsUnderAnyRoot("/etc/passwd"));
    }

    /// <summary>A sibling directory whose name merely starts with a root's name must not pass — the guard
    /// has to compare path segments, not string prefixes (".../manga" vs ".../manga-backup").</summary>
    [Fact]
    public void IsUnderAnyRoot_is_not_fooled_by_a_shared_name_prefix()
    {
        var paths = Build();
        var lookalike = paths.Root(MediaKind.Manga) + "-backup";

        Assert.False(paths.IsUnderAnyRoot(Path.Combine(lookalike, "Berserk", "1.cbz")));
    }

    /// <summary>An artifact's name is derived from series title + chapter number + group, none of which are
    /// unique — two inbox files can share a base name, and re-downloading a chapter reproduces the name it
    /// already has. Landing on an occupied path would overwrite bytes a live artifact row still points at.</summary>
    [Fact]
    public void UniquePath_only_diverts_when_something_is_already_there()
    {
        var paths = Build();
        var dir = paths.Root(MediaKind.Manga);
        var taken = Path.Combine(dir, "Berserk - Ch. 1.cbz");

        // Nothing there yet: the caller gets exactly the path it asked for.
        Assert.Equal(taken, LibraryPaths.UniquePath(taken));

        File.WriteAllText(taken, "the existing artifact's bytes");
        var diverted = LibraryPaths.UniquePath(taken);

        Assert.NotEqual(taken, diverted);
        Assert.False(File.Exists(diverted));
        Assert.Equal(dir, Path.GetDirectoryName(diverted));
        Assert.Equal(".cbz", Path.GetExtension(diverted));
        Assert.StartsWith("Berserk - Ch. 1-", Path.GetFileNameWithoutExtension(diverted));
        Assert.Equal("the existing artifact's bytes", File.ReadAllText(taken)); // untouched
    }

    /// <summary>Folder artifacts collide the same way files do, and a directory in the way is just as fatal
    /// (the second write would merge its pages into the first's folder).</summary>
    [Fact]
    public void UniquePath_diverts_around_an_existing_directory_too()
    {
        var paths = Build();
        var taken = Path.Combine(paths.Root(MediaKind.Manga), "Berserk - Ch. 1");
        Directory.CreateDirectory(taken);

        Assert.NotEqual(taken, LibraryPaths.UniquePath(taken));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try { Directory.Delete(_base, recursive: true); } catch { /* best effort */ }
    }
}
