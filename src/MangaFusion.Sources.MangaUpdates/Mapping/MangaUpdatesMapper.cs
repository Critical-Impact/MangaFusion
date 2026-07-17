using MangaFusion.Contracts.Models;
using MangaFusion.Sources.MangaUpdates.Dtos;

namespace MangaFusion.Sources.MangaUpdates.Mapping;

internal static class MangaUpdatesMapper
{
    public static SourceSeries ToSeries(SeriesModelDto dto)
    {
        var authors = dto.Authors?.Where(a => !string.Equals(a.Type, "Artist", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
        var artists = dto.Authors?.Where(a => string.Equals(a.Type, "Artist", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

        return new SourceSeries
        {
            SourceId = MangaUpdatesConstants.SourceId,
            SourceSeriesId = dto.SeriesId.ToString(),
            Title = dto.Title,
            AltTitles = dto.Associated?.Select(a => a.Title).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [],
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description,
            CoverUrl = dto.Image?.Url?.Original ?? dto.Image?.Url?.Thumb,
            Authors = authors.Select(a => a.Name).ToList(),
            Artists = artists.Select(a => a.Name).ToList(),
            AuthorRefs = authors.Select(a => new SourceAuthorRef(a.AuthorId.ToString(), a.Name)).ToList(),
            ArtistRefs = artists.Select(a => new SourceAuthorRef(a.AuthorId.ToString(), a.Name)).ToList(),
            Tags = dto.Genres?.Select(g => g.Genre).Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? [],
            ContentRating = ContentRating.Unknown, // MangaUpdates has no equivalent rating enum; left to the user to set.
            Status = MapStatus(dto.Status, dto.Completed),
            Year = int.TryParse(dto.Year, out var year) ? year : null,
            OriginalLanguage = MapOriginalLanguage(dto.Type),
        };
    }

    public static SourceTag ToTag(GenreStatsDto dto) => new(dto.Id.ToString(), dto.Genre, "Genre");

    private static PublicationStatus MapStatus(string? status, bool completed)
    {
        if (completed)
        {
            return PublicationStatus.Completed;
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            return PublicationStatus.Unknown;
        }

        return status.Contains("Complete", StringComparison.OrdinalIgnoreCase) ? PublicationStatus.Completed
            : status.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ? PublicationStatus.Cancelled
            : status.Contains("Hiatus", StringComparison.OrdinalIgnoreCase) ? PublicationStatus.Hiatus
            : status.Contains("Ongoing", StringComparison.OrdinalIgnoreCase) ? PublicationStatus.Ongoing
            : PublicationStatus.Unknown;
    }

    /// <summary>Best-effort guess from MangaUpdates' "type" field (Manga/Manhwa/Manhua/...) — MangaUpdates
    /// doesn't expose a real original-language field.</summary>
    private static string? MapOriginalLanguage(string? type) => type switch
    {
        "Manga" => "ja",
        "Manhwa" => "ko",
        "Manhua" => "zh",
        _ => null,
    };
}
