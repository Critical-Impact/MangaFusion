namespace MangaFusion.Domain.Library;

/// <summary>Which flavour of the app a row belongs to. The library is split along this axis: a series
/// is either manga (MangaDex, scanlation groups, translated languages, right-to-left) or comics
/// (ComicVine, publishers/characters/arcs, local files only). Kind lives on <see cref="Series"/> and
/// everything hanging off a series inherits it by navigation — only rows with no path back to a series
/// (tags, downloads, notifications, wizard batches) carry their own copy.</summary>
public enum MediaKind
{
    Manga = 0,
    Comic = 1,
}

/// <summary>Content rating (canonical domain copy; source enums are mapped onto this at the boundary).</summary>
public enum ContentRating
{
    Unknown = 0,
    Safe,
    Suggestive,
    Erotica,
    Pornographic,
}

public enum PublicationStatus
{
    Unknown = 0,
    Ongoing,
    Completed,
    Hiatus,
    Cancelled,
}

/// <summary>On-disk format of a downloaded artifact.</summary>
public enum StorageFormat
{
    Cbz = 0,
    Folder = 1,
}

public enum ArtifactStatus
{
    Pending = 0,
    Downloading,
    Complete,
    Failed,
}

public enum DownloadKind
{
    SingleRelease = 0,
    Volume = 1,
}

/// <summary>How an artifact came to exist — downloaded from a source, or manually imported from a
/// local file. Local artifacts are excluded from auto-replace so a hand-curated file is never deleted.</summary>
public enum ArtifactOrigin
{
    Download = 0,
    Local = 1,
}

public enum DownloadStatus
{
    Queued = 0,
    Running,
    Completed,
    Failed,
    Cancelled,
}
