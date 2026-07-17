using MangaFusion.Domain.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Resolves on-disk locations under the configured library roots and sanitizes names.
///
/// Manga and comics live under <b>separate roots</b>, so the two libraries can sit on different volumes
/// (comics on a NAS, manga on an SSD). The consequence is that <c>Artifact.Path</c> and
/// <c>Series.CoverPath</c> are relative to <em>their own kind's</em> root — a stored relative path is
/// meaningless without the kind, which is why every resolve site has to hand one in.
///
/// <c>Library:RootPath</c> remains a base that the two per-kind defaults derive from: one knob for a
/// simple setup, two for a split one.</summary>
public sealed class LibraryPaths
{
    private readonly IReadOnlyDictionary<MediaKind, string> _roots;

    /// <summary>Scratch-file work area for anything that needs a temp directory (PDF page
    /// rasterization, in-flight downloads before they're written into the library). Deliberately
    /// <em>not</em> the OS temp directory (<c>Path.GetTempPath()</c>) — that's frequently a small,
    /// RAM-backed tmpfs (a few hundred MB to a couple GB), especially in containers, and a single
    /// large PDF's rasterized pages can exceed that even though the actual data volume has plenty of
    /// room. Not split per kind: it's transient scratch, and <see cref="NewTempDirectory"/> already
    /// namespaces each caller's directory.</summary>
    public string TempRoot { get; }

    public LibraryPaths(IConfiguration config)
    {
        var baseRoot = config["Library:RootPath"] ?? "data/library";

        _roots = new Dictionary<MediaKind, string>
        {
            [MediaKind.Manga] = Resolve(
                config["Library:MangaRootPath"] ?? Path.Combine(baseRoot, MediaKindFolder.For(MediaKind.Manga))),
            [MediaKind.Comic] = Resolve(
                config["Library:ComicRootPath"] ?? Path.Combine(baseRoot, MediaKindFolder.For(MediaKind.Comic))),
        };

        TempRoot = Resolve(config["Library:TempPath"] ?? "data/tmp");

        static string Resolve(string path)
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(full);
            return full;
        }
    }

    public string Root(MediaKind kind) => _roots[kind];

    /// <summary>Every configured root. Used by the path-traversal guard, which only needs to know that a
    /// resolved file stays inside the library — not which half of it.</summary>
    public IReadOnlyCollection<string> AllRoots => _roots.Values.ToList();

    public string SeriesDirectory(MediaKind kind, string title) =>
        Path.Combine(Root(kind), Sanitize(title));

    /// <summary>Resolves a stored relative path (<c>Artifact.Path</c>, <c>Series.CoverPath</c>) against
    /// the root of the library it belongs to.</summary>
    public string Absolute(MediaKind kind, string relativePath) =>
        Path.Combine(Root(kind), relativePath);

    /// <summary>The inverse of <see cref="Absolute"/> — what gets persisted.</summary>
    public string RelativeTo(MediaKind kind, string absolutePath) =>
        Path.GetRelativePath(Root(kind), absolutePath);

    /// <summary>True when <paramref name="absolutePath"/> sits inside one of the library roots. A
    /// traversal guard, not a kind check: it answers "does this escape the library", and the caller is
    /// responsible for having resolved against the right root in the first place.</summary>
    public bool IsUnderAnyRoot(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        return _roots.Values.Any(root =>
        {
            var trimmed = Path.TrimEndingDirectorySeparator(root);
            return full == trimmed ||
                   full.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        });
    }

    /// <summary>Creates and returns a fresh, uniquely-named scratch subdirectory under
    /// <see cref="TempRoot"/>. The caller owns its lifetime and must delete it when done.</summary>
    public string NewTempDirectory(string prefix)
    {
        var dir = Path.Combine(TempRoot, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(cleaned) ? "_" : cleaned;
    }

    /// <summary><paramref name="path"/> if nothing is there, else the same name with a short random
    /// suffix. An artifact's name is derived from series title + chapter number + group, none of which
    /// are unique: two inbox files can share a base name, and re-downloading a chapter reproduces the
    /// name it already has. Letting a write land on an occupied path would overwrite the bytes of an
    /// artifact whose row still points at them — so every artifact write allocates a fresh path and the
    /// old file is only ever removed via its own artifact row.</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var dir = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(dir, $"{name}-{suffix}{ext}");
    }
}
