using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Downloads a source's cover image into the series' library directory. Shared by
/// add-to-library (<see cref="LibraryService"/>) and the migration tool
/// (<see cref="MigrationCommitter"/>) — best-effort: failures are logged, never thrown.</summary>
public sealed class SeriesCoverCache(
    IHttpClientFactory httpFactory, LibraryPaths paths, CollectionCoverComposer coverComposer,
    ILogger<SeriesCoverCache> logger)
{
    public async Task TryCacheAsync(Series series, string? coverUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(coverUrl) || series.LockedFields.HasFlag(SeriesLockedFields.Cover))
        {
            return;
        }

        try
        {
            using var client = httpFactory.CreateClient(LibraryService.ImageClientName);
            using var response = await client.GetAsync(coverUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var directory = paths.SeriesDirectory(series.Kind, series.Title);
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, "cover.jpg");
            await using (var stream = File.Create(file))
            {
                await response.Content.CopyToAsync(stream, ct);
            }

            series.CoverPath = paths.RelativeTo(series.Kind, file);
            series.CoverUpdatedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cache cover for {Series}", series.Title);
        }
    }

    /// <summary>Validates + stores a user-uploaded cover over the series' existing <c>cover.jpg</c> —
    /// unlike <see cref="TryCacheAsync"/>, the caller (an admin-only endpoint) is responsible for
    /// rejecting a bad image, so this returns whether the upload was actually applied.</summary>
    public async Task<bool> SetCustomCoverAsync(Series series, Stream image, CancellationToken ct)
    {
        var directory = paths.SeriesDirectory(series.Kind, series.Title);
        var file = await coverComposer.StoreCustomAsync(directory, "cover.jpg", image, ct);
        if (file is null)
        {
            return false;
        }

        series.CoverPath = paths.RelativeTo(series.Kind, file);
        series.CoverUpdatedAt = DateTimeOffset.UtcNow;
        return true;
    }
}
