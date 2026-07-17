using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Downloads a source's cover image into the series' library directory. Shared by
/// add-to-library (<see cref="LibraryService"/>) and the migration tool
/// (<see cref="MigrationCommitter"/>) — best-effort: failures are logged, never thrown.</summary>
public sealed class SeriesCoverCache(
    IHttpClientFactory httpFactory, LibraryPaths paths, ILogger<SeriesCoverCache> logger)
{
    public async Task TryCacheAsync(Series series, string? coverUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(coverUrl))
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
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to cache cover for {Series}", series.Title);
        }
    }
}
