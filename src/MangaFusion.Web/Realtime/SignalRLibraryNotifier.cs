using MangaFusion.Application.Realtime;
using MangaFusion.Domain.Library;
using Microsoft.AspNetCore.SignalR;

namespace MangaFusion.Web.Realtime;

public sealed class SignalRLibraryNotifier(IHubContext<LibraryHub> hub) : ILibraryNotifier
{
    public Task DownloadProgressAsync(
        Guid downloadId, Guid? chapterId, DownloadStatus status, int pagesDone, int pagesTotal,
        CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(
            "downloadProgress",
            new
            {
                downloadId,
                chapterId,
                status = status.ToString(),
                pagesDone,
                pagesTotal,
            },
            ct);

    public Task ImportCommitProgressAsync(
        Guid importSeriesId, string status, int itemsDone, int itemsTotal, int? pageDone, int? pageTotal,
        CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(
            "importCommitProgress",
            new
            {
                importSeriesId,
                status,
                itemsDone,
                itemsTotal,
                pageDone,
                pageTotal,
            },
            ct);

    public Task MigrationCommitProgressAsync(
        Guid migrationSeriesId, string status, int itemsDone, int itemsTotal, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(
            "migrationCommitProgress",
            new { migrationSeriesId, status, itemsDone, itemsTotal },
            ct);

    public Task MigrationBatchCommitProgressAsync(
        Guid batchId, int seriesDone, int seriesTotal, CancellationToken ct = default) =>
        hub.Clients.All.SendAsync(
            "migrationBatchCommitProgress",
            new { batchId, seriesDone, seriesTotal },
            ct);

    public Task NotificationAsync(Guid userId, string title, string? body, CancellationToken ct = default) =>
        hub.Clients.User(userId.ToString()).SendAsync("notification", new { title, body }, ct);
}
