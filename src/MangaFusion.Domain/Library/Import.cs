namespace MangaFusion.Domain.Library;

public enum ImportBatchStatus
{
    Scanning = 0,
    Done = 1,
    Failed = 2,
}

public enum ImportSeriesStatus
{
    /// <summary>Always the resting state after a scan — this wizard never auto-commits, since
    /// title-only matching against MangaUpdates is not reliable enough to trust unattended (see
    /// MigrationMatcher's title-matching notes for the same lesson in the CBZ-migration tool).</summary>
    NeedsReview = 0,
    Committed = 1,
    Failed = 2,

    /// <summary>Commit is running as a background job (PDF conversion can take minutes) — see
    /// <see cref="ImportSeries"/>'s Commit* progress fields.</summary>
    Committing = 3,
}

/// <summary>How to read pages out of an import item's source file — mirrors
/// <c>MangaFusion.Infrastructure.Library.ChapterSourceKind</c> (kept separate: Domain doesn't
/// reference Infrastructure), which the wizard's committer maps this onto 1:1.</summary>
public enum ImportSourceFormat
{
    Cbz = 0,
    Folder = 1,
    Pdf = 2,
    Cbr = 3,
    Epub = 4,
}

/// <summary>One run of the MangaUpdates-assisted import wizard over the import inbox. One group of
/// same-title inbox subfolders becomes one <see cref="ImportSeries"/>.</summary>
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Scanning;
    public string? Error { get; set; }

    /// <summary>Which library this batch commits into — and therefore which metadata source its series
    /// are matched against (manga → MangaUpdates, comics → ComicVine).</summary>
    public MediaKind Kind { get; set; } = MediaKind.Manga;

    public List<ImportSeries> Series { get; set; } = [];
}

/// <summary>One group of inbox subfolders believed to be the same series, its MangaUpdates match (if
/// any), and its per-file import plan. Nothing is imported until the user explicitly commits — this
/// wizard never auto-commits a match.</summary>
public class ImportSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }
    public ImportBatch Batch { get; set; } = default!;

    /// <summary>Best-effort title parsed from the inbox folder name(s) — the initial (fully editable)
    /// search seed. Never authoritative.</summary>
    public string GroupTitle { get; set; } = default!;

    /// <summary>MangaUpdates series id/title the user picked, if any. The source is implicitly
    /// "mangaupdates" — this wizard only ever matches against that one provider.</summary>
    public string? MatchedSourceSeriesId { get; set; }
    public string? MatchedTitle { get; set; }

    /// <summary>Overrides <see cref="MatchedTitle"/> as the committed series' title — e.g. picking one
    /// of MangaUpdates' alt-titles (often the English release title) over its primary title (often a
    /// romanized-Japanese one). Null uses the matched/primary title as-is. Ignored when merging into
    /// an existing library series, whose title is never touched.</summary>
    public string? TitleOverride { get; set; }

    public ImportSeriesStatus Status { get; set; } = ImportSeriesStatus.NeedsReview;

    /// <summary>An existing library series to merge into instead of creating a new one.</summary>
    public Guid? ExistingLibrarySeriesId { get; set; }

    /// <summary>The library series this import resolved to — created, matched, or merged into. Set as
    /// soon as it's known (not at the end), because a commit can fail part-way and its retry has to land
    /// in the same series rather than create a second one. So this being set means "this import has a
    /// series", not "this import finished" — <see cref="Status"/> is what says it finished.</summary>
    public Guid? CommittedLibrarySeriesId { get; set; }

    // --- Commit progress (while Status == Committing) — persisted periodically as a durable fallback
    // for the live SignalR push; polling picks these up after a page refresh/reconnect. ---
    public int? CommitItemsDone { get; set; }
    public int? CommitItemsTotal { get; set; }
    public int? CommitPageDone { get; set; }
    public int? CommitPageTotal { get; set; }

    /// <summary>Set when a commit attempt fails; the series reverts to NeedsReview (not a dead-end
    /// Failed state) so the user can fix the issue and immediately retry.</summary>
    public string? CommitError { get; set; }

    public List<ImportItem> Items { get; set; } = [];
}

/// <summary>One inbox subfolder/file discovered under an import series' group, with its parsed volume
/// guess and the (editable) chapter spec that will be used to carve it on commit.</summary>
public class ImportItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ImportSeriesId { get; set; }
    public ImportSeries Series { get; set; } = default!;

    public string FolderName { get; set; } = default!;
    public string FileName { get; set; } = default!;
    public ImportSourceFormat Format { get; set; }

    public string? ParsedVolume { get; set; }
    public int PageCount { get; set; }
    public long SizeBytes { get; set; }

    /// <summary>Whether this item is imported on commit — user-togglable (e.g. to skip a stray/
    /// corrupt file without dropping the whole series group).</summary>
    public bool Include { get; set; } = true;

    /// <summary>Chapter number/volume/title to import this file as — pre-filled from
    /// <see cref="ParsedVolume"/> by the scanner, fully user-editable thereafter.</summary>
    public string? Number { get; set; }
    public string? Volume { get; set; }
    public string? Title { get; set; }

    /// <summary>When this item's chapter was written into the library and its source file removed from
    /// the inbox — null until then. A commit is not atomic (each item is a separate file write plus an
    /// irreversible inbox delete), so this is what makes a retry a *resume*: a commit that dies on item
    /// 3 of 5 must not re-import items 1-2, whose source files are already gone and whose chapters would
    /// now collide with themselves. See ImportCommitter.CommitAsync.</summary>
    public DateTimeOffset? ImportedAt { get; set; }
}
