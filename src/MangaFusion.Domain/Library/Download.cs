namespace MangaFusion.Domain.Library;

/// <summary>Queue/history record for a download, mirroring its Hangfire job. Backs the activity UI
/// and retries.</summary>
public class Download
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SeriesId { get; set; }
    public DownloadKind Kind { get; set; }

    /// <summary>Which library this download belongs to. Carried rather than inherited because
    /// <see cref="SeriesId"/> is a bare id with no navigation to join through. Named in full to avoid
    /// colliding with <see cref="Kind"/>, which already means something else here.</summary>
    public MediaKind MediaKind { get; set; } = MediaKind.Manga;

    /// <summary>Target release for <see cref="DownloadKind.SingleRelease"/>.</summary>
    public Guid? ReleaseId { get; set; }
    public Guid? ChapterId { get; set; }

    public string? Description { get; set; }
    public DownloadStatus Status { get; set; } = DownloadStatus.Queued;
    public int PagesDone { get; set; }
    public int PagesTotal { get; set; }
    public string? Error { get; set; }
    public string? HangfireJobId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
