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

/// <summary>How a <see cref="Collection"/>'s members are ordered on its page. <see cref="Manual"/>
/// honours each member's stored position; the rest are computed from series metadata at query time.</summary>
public enum MemberSort
{
    Manual = 0,
    TitleAsc,
    TitleDesc,
    RecentlyAdded,
    Year,
}

/// <summary>Which members of a <see cref="Collection"/> surface on the Home dashboard rail. A filter
/// that hides every member also hides the rail (an empty rail doesn't render). Kept as an enum so more
/// filters can be added later without another migration.</summary>
public enum CollectionDashboardFilter
{
    /// <summary>Every member shows.</summary>
    All = 0,

    /// <summary>Only members with at least one downloaded chapter the current user hasn't finished
    /// reading — the dashboard is a read-now surface, so a fully-read or undownloaded series is noise.</summary>
    Unread = 1,
}

/// <summary>How a series' chapters are ordered. <see cref="Absolute"/> (the default) sorts purely by
/// <see cref="Chapter.NumberSort"/>/<see cref="Chapter.NumberKey"/> as always. <see cref="VolumeThenChapter"/>
/// sorts by volume first and chapter number second within that volume — for manually-imported series
/// that mix whole-volume compilation files with individually-numbered extras meant to be read right
/// after a specific volume, where the extra's own number would otherwise collide with an unrelated
/// volume's number on the same absolute scale.</summary>
public enum ChapterSortMode
{
    Absolute = 0,
    VolumeThenChapter = 1,
}
