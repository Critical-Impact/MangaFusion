using MangaFusion.Domain.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Resolves the metadata-assisted import wizard's inbox, split per library
/// (<c>&lt;root&gt;/manga</c>, <c>&lt;root&gt;/comics</c>) so a comic and a manga release can't sit in the
/// same folder waiting to be claimed by whichever kind the user happened to start the scan with — which
/// is also what decides the metadata source they're matched against (MangaUpdates vs ComicVine).
///
/// Deliberately separate from the Local and Migrate tools' roots: this wizard expects one subfolder per
/// release (scene/publisher-style names, no ComicInfo.xml), a different shape than either of those
/// scanners understands, so a shared root would cause folders to be silently ignored (harmless) or
/// ambiguously double-claimed (not harmless).</summary>
public sealed class ImportPaths
{
    private readonly IConfiguration _config;

    public ImportPaths(IConfiguration config)
    {
        _config = config;
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            Directory.CreateDirectory(InboxRoot(kind));
        }
    }

    public string InboxRoot(MediaKind kind)
    {
        var root = _config["Import:InboxPath"] ?? "data/import-inbox";
        return Path.GetFullPath(Path.Combine(root, MediaKindFolder.For(kind)));
    }

    public string SeriesInboxFolder(MediaKind kind, string folderName) =>
        Path.Combine(InboxRoot(kind), folderName);
}
