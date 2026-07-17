namespace MangaFusion.Domain.Library;

public enum MigrationBatchStatus
{
    Scanning = 0,
    Done = 1,
    Failed = 2,
}

/// <summary>Whether a series' chapters are still on the source (Live), have been purged from the
/// source's feed — e.g. a licensed release (Purged), only partially present (Mixed), or no source
/// series could be confidently identified (Unmatched).</summary>
public enum MigrationRegime
{
    Unknown = 0,
    Live = 1,
    Purged = 2,
    Mixed = 3,
    Unmatched = 4,
}

public enum MigrationSeriesStatus
{
    Scanning = 0,
    NeedsReview = 1,
    Committed = 2,
    Failed = 3,
}

public enum MigrationItemDisposition
{
    /// <summary>Not yet classified (scan in progress).</summary>
    Pending = 0,

    /// <summary>Will be moved into the library as the active file for its chapter.</summary>
    Import = 1,

    /// <summary>A losing copy of a chapter that already has a winner — moved to the outbox.</summary>
    Duplicate = 2,

    /// <summary>Failed the integrity filter (no image pages / suspiciously small) — moved to the
    /// outbox, never imported.</summary>
    Quarantine = 3,

    /// <summary>Ambiguous (e.g. colliding UUID prefixes) — held for manual resolution.</summary>
    Unresolved = 4,
}

/// <summary>One run of the CBZ migration tool over the inbox. One subfolder under the inbox root
/// becomes one <see cref="MigrationSeries"/> — except a folder with no ComicInfo.xml in any of its
/// files, which never becomes one; its name is recorded in <see cref="DivertedFolders"/> instead
/// (moved into the import wizard's inbox by the Infrastructure-layer scan/service).</summary>
public class MigrationBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MigrationBatchStatus Status { get; set; } = MigrationBatchStatus.Scanning;
    public string? Error { get; set; }

    /// <summary>Which library this batch commits into. Always <see cref="MediaKind.Manga"/> in practice —
    /// the CBZ migration tool matches against MangaDex and dedups by scanlation group — but carried so
    /// batches stay filterable alongside the import wizard's.</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    /// <summary>Folder names moved into the import wizard's inbox instead of being scanned as
    /// migration candidates, because none of their files had a ComicInfo.xml — almost certainly not
    /// from the old MangaDex downloader this tool targets.</summary>
    public List<string> DivertedFolders { get; set; } = [];

    public List<MigrationSeries> Series { get; set; } = [];
}

/// <summary>One inbox subfolder being migrated: the matched (or unmatched) source series, its
/// regime, and the per-file plan. Nothing under this series is moved until it reaches
/// <see cref="MigrationSeriesStatus.Committed"/> — either automatically (no conflicts) or after
/// manual review clears <see cref="ConflictReason"/>.</summary>
public class MigrationSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public MigrationBatch Batch { get; set; } = default!;

    /// <summary>Inbox subfolder name (also the fallback series title).</summary>
    public string FolderName { get; set; } = default!;

    /// <summary>Most common <c>&lt;Series&gt;</c> value read from the folder's ComicInfo files.</summary>
    public string? ComicInfoSeriesTitle { get; set; }

    public string? MatchedSourceId { get; set; }
    public string? MatchedSourceSeriesId { get; set; }
    public string? MatchedTitle { get; set; }

    public MigrationRegime Regime { get; set; } = MigrationRegime.Unknown;

    /// <summary>Fraction (0..1) of local files whose UUID prefix was found in the matched source's
    /// feed. Drives auto-commit eligibility.</summary>
    public double Confidence { get; set; }

    public MigrationSeriesStatus Status { get; set; } = MigrationSeriesStatus.Scanning;

    /// <summary>Human-readable reason this series is held for review; null once clear.</summary>
    public string? ConflictReason { get; set; }

    /// <summary>An existing library series to merge into instead of creating a new one. Its own
    /// metadata is never overwritten by the migration — only chapters/files are added.</summary>
    public Guid? ExistingLibrarySeriesId { get; set; }

    /// <summary>The library series this migration series committed into, once committed.</summary>
    public Guid? CommittedLibrarySeriesId { get; set; }

    /// <summary>Scanlation-group preference ranked by frequency across this series' matched
    /// releases (most common first) — becomes <see cref="Series.PreferredGroups"/> on commit.</summary>
    public List<string> GroupRanking { get; set; } = [];

    public List<MigrationItem> Items { get; set; } = [];
}

/// <summary>One local CBZ/folder file discovered in a migration series' inbox folder.</summary>
public class MigrationItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MigrationSeriesId { get; set; }
    public MigrationSeries Series { get; set; } = default!;

    public string FileName { get; set; } = default!;

    /// <summary>First 8 hex chars of the filename's trailing UUID segment (the old downloader's
    /// chapter-id fragment), if present.</summary>
    public string? UuidPrefix { get; set; }

    /// <summary>Chapter number from ComicInfo's &lt;Number&gt; — authoritative over the filename.</summary>
    public string? Number { get; set; }

    /// <summary>Normalized dedup key for <see cref="Number"/> (language is always "en").</summary>
    public string NumberKey { get; set; } = default!;

    public string? ChapterTitle { get; set; }

    public int PageCount { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Full source chapter id resolved by UUID-prefix match against the source feed; null
    /// when the source no longer has this chapter (purged) or nothing matched.</summary>
    public string? MatchedSourceChapterId { get; set; }

    /// <summary>Scanlation group recovered from the matched source release; null when unmatched.</summary>
    public string? MatchedGroup { get; set; }

    public MigrationItemDisposition Disposition { get; set; } = MigrationItemDisposition.Pending;

    /// <summary>True if this is the file that will be imported for its (Series, NumberKey) group.</summary>
    public bool IsWinner { get; set; }

    /// <summary>Why this item is flagged/quarantined/a duplicate, for the review UI.</summary>
    public string? FlagReason { get; set; }
}
