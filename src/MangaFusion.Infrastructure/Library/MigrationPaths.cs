using MangaFusion.Domain.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Resolves the migration tool's inbox and outbox (duplicate/quarantined files set aside for the
/// user).
///
/// The <b>inbox is not split per kind</b>: this tool only ingests the old MangaDex downloader's output —
/// it matches files by their MangaDex chapter-UUID filename prefix and dedups by scanlation group, neither
/// of which has a ComicVine analogue — so it is manga-only by construction and a comics half would never
/// be usable. The <b>outbox is split</b>, to keep the on-disk layout symmetric with the inboxes.
///
/// Its keys moved off the <c>LocalImport:</c> prefix (which it used to share with the Local tool, whose
/// inbox resolution was a copy-paste of this one) so Local could become per-kind without dragging the
/// migration tool along.</summary>
public sealed class MigrationPaths
{
    private readonly IConfiguration _config;

    public MigrationPaths(IConfiguration config)
    {
        _config = config;
        Directory.CreateDirectory(InboxRoot());
        foreach (var kind in Enum.GetValues<MediaKind>())
        {
            Directory.CreateDirectory(OutboxRoot(kind));
        }
    }

    public string InboxRoot()
    {
        var root = _config["Migrate:InboxPath"] ?? "data/migrate-inbox";
        return Path.GetFullPath(root);
    }

    public string OutboxRoot(MediaKind kind)
    {
        var root = _config["Migrate:OutboxPath"] ?? "data/outbox";
        return Path.GetFullPath(Path.Combine(root, MediaKindFolder.For(kind)));
    }

    public string SeriesInboxFolder(string folderName) => Path.Combine(InboxRoot(), folderName);

    public string SeriesOutboxFolder(MediaKind kind, string folderName)
    {
        var dir = Path.Combine(OutboxRoot(kind), LibraryPaths.Sanitize(folderName));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
