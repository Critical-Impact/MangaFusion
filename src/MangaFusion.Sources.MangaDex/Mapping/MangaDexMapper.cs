using MangaFusion.Contracts.Models;
using MangaFusion.Sources.MangaDex.Dtos;

namespace MangaFusion.Sources.MangaDex.Mapping;

internal static class MangaDexMapper
{
    private const string PreferredLanguage = "en";

    public static SourceSeries ToSeries(MangaDataDto dto)
    {
        var a = dto.Attributes;
        var coverFile = dto.Relationships
            .FirstOrDefault(r => r.Type == "cover_art")?.Attributes?.FileName;

        return new SourceSeries
        {
            SourceId = MangaDexConstants.SourceId,
            SourceSeriesId = dto.Id,
            Title = PickLocalized(a.Title) ?? "Untitled",
            AltTitles = a.AltTitles?.SelectMany(d => d.Values).Distinct().ToList() ?? [],
            Description = PickLocalized(a.Description),
            CoverUrl = coverFile is null ? null : BuildCoverUrl(dto.Id, coverFile),
            Authors = RelationshipNames(dto, "author"),
            Artists = RelationshipNames(dto, "artist"),
            AuthorRefs = RelationshipRefs(dto, "author"),
            ArtistRefs = RelationshipRefs(dto, "artist"),
            Tags = a.Tags?.Select(t => PickLocalized(t.Attributes.Name)).OfType<string>().ToList() ?? [],
            TagRefs = a.Tags?
                .Select(t => (Id: t.Id, Name: PickLocalized(t.Attributes.Name), Group: t.Attributes.Group))
                .Where(t => t.Name is not null)
                .Select(t => new SourceTagRef(t.Id, t.Name!, t.Group ?? "other"))
                .ToList() ?? [],
            ContentRating = MapRating(a.ContentRating),
            Status = MapStatus(a.Status),
            Year = a.Year,
            OriginalLanguage = a.OriginalLanguage,
            AvailableTranslatedLanguages = a.AvailableTranslatedLanguages ?? [],
            LastChapter = string.IsNullOrWhiteSpace(a.LastChapter) ? null : a.LastChapter,

            // MangaDex has no issue-count equivalent (LastChapter is a number, not a count), but its title
            // page is reconstructible from the id alone — unlike ComicVine, whose URLs carry a slug.
            SiteUrl = $"https://mangadex.org/title/{dto.Id}",
        };
    }

    public static SourceChapter ToChapter(ChapterDataDto dto)
    {
        var a = dto.Attributes;
        return new SourceChapter
        {
            SourceId = MangaDexConstants.SourceId,
            SourceChapterId = dto.Id,
            Volume = a.Volume,
            Number = a.Chapter,
            Title = string.IsNullOrWhiteSpace(a.Title) ? null : a.Title,
            Language = a.TranslatedLanguage ?? "unknown",
            ScanlationGroups = RelationshipNames(dto, "scanlation_group"),
            PageCount = a.Pages,
            PublishedAt = a.PublishAt,
            IsExternal = !string.IsNullOrEmpty(a.ExternalUrl),
            ExternalUrl = a.ExternalUrl,
        };
    }

    public static SourceTag ToTag(TagEntityDto dto) =>
        new(dto.Id, PickLocalized(dto.Attributes.Name) ?? dto.Id, dto.Attributes.Group ?? "other");

    /// <summary>512px thumbnail of a cover. Full-size is the same URL without the size suffix.</summary>
    public static string BuildCoverUrl(string mangaId, string fileName) =>
        $"{MangaDexConstants.UploadsBaseUrl}/covers/{mangaId}/{fileName}.512.jpg";

    private static List<string> RelationshipNames(MangaDataDto dto, string type) =>
        dto.Relationships.Where(r => r.Type == type)
            .Select(r => r.Attributes?.Name).OfType<string>().ToList();

    private static List<SourceAuthorRef> RelationshipRefs(MangaDataDto dto, string type) =>
        dto.Relationships.Where(r => r.Type == type && r.Attributes?.Name is not null)
            .Select(r => new SourceAuthorRef(r.Id, r.Attributes!.Name!)).ToList();

    private static List<string> RelationshipNames(ChapterDataDto dto, string type) =>
        dto.Relationships.Where(r => r.Type == type)
            .Select(r => r.Attributes?.Name).OfType<string>().ToList();

    private static string? PickLocalized(Dictionary<string, string>? localized)
    {
        if (localized is null || localized.Count == 0)
        {
            return null;
        }

        return localized.TryGetValue(PreferredLanguage, out var preferred)
            ? preferred
            : localized.Values.FirstOrDefault();
    }

    private static ContentRating MapRating(string? value) => value switch
    {
        "safe" => ContentRating.Safe,
        "suggestive" => ContentRating.Suggestive,
        "erotica" => ContentRating.Erotica,
        "pornographic" => ContentRating.Pornographic,
        _ => ContentRating.Unknown,
    };

    private static PublicationStatus MapStatus(string? value) => value switch
    {
        "ongoing" => PublicationStatus.Ongoing,
        "completed" => PublicationStatus.Completed,
        "hiatus" => PublicationStatus.Hiatus,
        "cancelled" => PublicationStatus.Cancelled,
        _ => PublicationStatus.Unknown,
    };

    // Request-side mappings (enum -> MangaDex query values).

    public static string? RatingToApi(ContentRating rating) => rating switch
    {
        ContentRating.Safe => "safe",
        ContentRating.Suggestive => "suggestive",
        ContentRating.Erotica => "erotica",
        ContentRating.Pornographic => "pornographic",
        _ => null,
    };

    public static string? StatusToApi(PublicationStatus status) => status switch
    {
        PublicationStatus.Ongoing => "ongoing",
        PublicationStatus.Completed => "completed",
        PublicationStatus.Hiatus => "hiatus",
        PublicationStatus.Cancelled => "cancelled",
        _ => null,
    };
}
