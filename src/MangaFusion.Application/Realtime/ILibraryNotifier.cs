using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Realtime;

/// <summary>Pushes live library/download updates to connected clients. Implemented in the web layer
/// over SignalR; the download engine depends only on this abstraction.</summary>
public interface ILibraryNotifier
{
    Task DownloadProgressAsync(
        Guid downloadId, Guid? chapterId, DownloadStatus status, int pagesDone, int pagesTotal,
        CancellationToken ct = default);

    /// <summary>Live progress for the MangaUpdates import wizard's commit step (PDF conversion can
    /// take minutes) — <paramref name="pageDone"/>/<paramref name="pageTotal"/> are null when the
    /// current item isn't a PDF (nothing slow enough to report progress on).</summary>
    Task ImportCommitProgressAsync(
        Guid importSeriesId, string status, int itemsDone, int itemsTotal, int? pageDone, int? pageTotal,
        CancellationToken ct = default);

    /// <summary>Pushes a new-notification signal to a specific user so their bell updates live.</summary>
    Task NotificationAsync(Guid userId, string title, string? body, CancellationToken ct = default);
}
