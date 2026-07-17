using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using DomainContentRating = MangaFusion.Domain.Library.ContentRating;
using DomainStatus = MangaFusion.Domain.Library.PublicationStatus;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Applies a source's series metadata (title, tags, rating, ...) onto a domain
/// <see cref="Series"/> — shared by the initial add (<see cref="LibraryService.AddSeriesAsync"/>) and
/// periodic rescans (<c>MonitorService</c>), so both keep metadata — including resolved Tag
/// associations — in sync with the source rather than only ever setting it once at add time.</summary>
public sealed class SeriesMetadataApplier(AuthorResolver authors, TagResolver tags)
{
    public async Task ApplyAsync(Series series, SourceSeries source, CancellationToken ct)
    {
        series.Title = source.Title;
        series.AltTitles = source.AltTitles.ToList();
        series.Description = source.Description;
        series.Authors = source.AuthorRefs.Count > 0
            ? await authors.ResolveSourceAuthorsAsync(source.SourceId, source.AuthorRefs, ct)
            : await authors.ResolveOrCreateByNameAsync(source.Authors, ct);
        series.Artists = source.ArtistRefs.Count > 0
            ? await authors.ResolveSourceAuthorsAsync(source.SourceId, source.ArtistRefs, ct)
            : await authors.ResolveOrCreateByNameAsync(source.Artists, ct);
        // Tags are kind-scoped, and the series already knows which library it lives in.
        series.Tags = source.TagRefs.Count > 0
            ? await tags.ResolveSourceTagsAsync(series.Kind, source.SourceId, source.TagRefs, ct)
            : await tags.ResolveOrCreateByNameAsync(series.Kind, source.Tags, ct);
        series.ContentRating = MapRating(source.ContentRating);
        series.Status = MapStatus(source.Status);
        series.Year = source.Year;
        series.OriginalLanguage = source.OriginalLanguage;
    }

    private static DomainContentRating MapRating(Contracts.Models.ContentRating rating) => rating switch
    {
        Contracts.Models.ContentRating.Safe => DomainContentRating.Safe,
        Contracts.Models.ContentRating.Suggestive => DomainContentRating.Suggestive,
        Contracts.Models.ContentRating.Erotica => DomainContentRating.Erotica,
        Contracts.Models.ContentRating.Pornographic => DomainContentRating.Pornographic,
        _ => DomainContentRating.Unknown,
    };

    private static DomainStatus MapStatus(Contracts.Models.PublicationStatus status) => status switch
    {
        Contracts.Models.PublicationStatus.Ongoing => DomainStatus.Ongoing,
        Contracts.Models.PublicationStatus.Completed => DomainStatus.Completed,
        Contracts.Models.PublicationStatus.Hiatus => DomainStatus.Hiatus,
        Contracts.Models.PublicationStatus.Cancelled => DomainStatus.Cancelled,
        _ => DomainStatus.Unknown,
    };
}
