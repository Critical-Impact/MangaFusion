namespace MangaFusion.Sources.MangaUpdates.Dtos;

// Minimal DTOs for the MangaUpdates JSON we consume (see the provided openapi.json). Deserialized
// with JsonNamingPolicy.SnakeCaseLower — the API's fields are snake_case (series_id, author_id, ...).

internal sealed class SeriesSearchResponseDto
{
    public int TotalHits { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public List<SeriesSearchResultDto> Results { get; set; } = [];
}

internal sealed class SeriesSearchResultDto
{
    public SeriesModelDto? Record { get; set; }
}

internal sealed class SeriesModelDto
{
    public long SeriesId { get; set; }
    public string Title { get; set; } = "";
    public List<AssociatedTitleDto>? Associated { get; set; }
    public string? Description { get; set; }
    public ImageModelDto? Image { get; set; }
    public string? Type { get; set; }
    public string? Year { get; set; }
    public List<GenreDto>? Genres { get; set; }
    public string? Status { get; set; }
    public bool Completed { get; set; }
    public List<SeriesAuthorDto>? Authors { get; set; }
}

internal sealed class AssociatedTitleDto
{
    public string Title { get; set; } = "";
}

internal sealed class ImageModelDto
{
    public ImageUrlDto? Url { get; set; }
}

internal sealed class ImageUrlDto
{
    public string? Original { get; set; }
    public string? Thumb { get; set; }
}

internal sealed class GenreDto
{
    public string Genre { get; set; } = "";
}

internal sealed class SeriesAuthorDto
{
    public string Name { get; set; } = "";
    public long AuthorId { get; set; }

    /// <summary>"Author" (writer) or "Artist".</summary>
    public string Type { get; set; } = "";
}

internal sealed class GenreStatsDto
{
    public long Id { get; set; }
    public string Genre { get; set; } = "";
}
