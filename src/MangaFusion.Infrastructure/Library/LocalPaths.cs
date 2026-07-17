using MangaFusion.Domain.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Resolves the manual/local import tool's inbox, split per library.
///
/// This used to share a root with the Migrate tool (both read <c>LocalImport:InboxPath</c>, defaulting to
/// <c>data/migrate-inbox</c>), with the resolution logic copy-pasted into <see cref="LocalImportService"/>.
/// They're now separate: Migrate is a manga-only tool (it matches MangaDex chapter-UUID filename prefixes
/// and dedups by scanlation group), so it can't follow Local into a per-kind layout.</summary>
public sealed class LocalPaths
{
    private readonly IConfiguration _config;

    public LocalPaths(IConfiguration config)
    {
        _config = config;
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            Directory.CreateDirectory(InboxRoot(kind));
        }
    }

    public string InboxRoot(MediaKind kind)
    {
        var root = _config["LocalImport:InboxPath"] ?? "data/local-inbox";
        return Path.GetFullPath(Path.Combine(root, MediaKindFolder.For(kind)));
    }
}
