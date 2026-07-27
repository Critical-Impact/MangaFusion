using System.Xml;
using System.Xml.Linq;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Builds a ComicInfo.xml for an artifact (the de-facto metadata standard read by Komga,
/// Kavita, Mihon, YACReader, ...). Field mapping follows Komga's documented ComicInfo ingestion
/// (https://komga.org/docs/guides/scan-analysis-refresh/) and the schema's AgeRating/Manga enums
/// (https://github.com/anansi-project/comicinfo).</summary>
internal static class ComicInfoXml
{
    public static async Task WriteAsync(Stream target, WriteRequest request, int pageCount, CancellationToken ct)
    {
        var first = request.Segments[0];
        var last = request.Segments[^1];
        var number = request.Segments.Count == 1
            ? first.Number
            : $"{first.Number}-{last.Number}";

        var published = first.PublishedAt?.ToUniversalTime();
        var notes = request.AltTitles is { Count: > 0 }
            ? $"Alternate titles: {string.Join(", ", request.AltTitles)}"
            : null;

        var root = new XElement("ComicInfo",
            first.Title is null ? null : new XElement("Title", first.Title),
            new XElement("Series", request.SeriesTitle),
            number is null ? null : new XElement("Number", number),
            first.Volume is null ? null : new XElement("Volume", first.Volume),
            request.Description is null ? null : new XElement("Summary", request.Description),
            notes is null ? null : new XElement("Notes", notes),
            published is null ? null : new XElement("Year", published.Value.Year),
            published is null ? null : new XElement("Month", published.Value.Month),
            published is null ? null : new XElement("Day", published.Value.Day),
            request.Authors.Count > 0 ? new XElement("Writer", string.Join(", ", request.Authors)) : null,
            request.Artists is { Count: > 0 } ? new XElement("Penciller", string.Join(", ", request.Artists)) : null,
            request.Artists is { Count: > 0 } ? new XElement("CoverArtist", string.Join(", ", request.Artists)) : null,
            first.Group is null ? null : new XElement("Translator", first.Group),
            request.Genres.Count > 0 ? new XElement("Genre", string.Join(", ", request.Genres)) : null,
            request.OtherTags is { Count: > 0 } ? new XElement("Tags", string.Join(", ", request.OtherTags)) : null,
            first.SourceUrl is null ? null : new XElement("Web", first.SourceUrl),
            new XElement("PageCount", pageCount),
            new XElement("LanguageISO", first.Language),
            MapAgeRating(request.ContentRating) is { } age ? new XElement("AgeRating", age) : null,
            new XElement("Manga", MapManga(request.Kind, request.OriginalLanguage)));

        var settings = new XmlWriterSettings { Async = true, Indent = true };
        await using var writer = XmlWriter.Create(target, settings);
        await new XDocument(root).SaveAsync(writer, ct);
    }

    /// <summary>The schema's MangaEnum: "No" | "Yes" | "YesAndRightToLeft" | "Unknown". A comic is always
    /// "No" — an exported comic that claims to be manga makes Komga/Kavita page it backwards. A light novel
    /// is likewise "No" (it's prose, exported as EPUB, never a right-to-left image reader). Only for manga
    /// does the original language decide between the left-to-right and right-to-left variants.</summary>
    private static string MapManga(MediaKind kind, string? originalLanguage) => kind switch
    {
        MediaKind.Comic => "No",
        MediaKind.LightNovel => "No",
        _ => MangaLanguage.IsRightToLeft(originalLanguage) ? "YesAndRightToLeft" : "Yes",
    };

    // Maps our simplified rating scale onto the schema's AgeRatingEnum (a fixed string enum, not
    // freeform) — see https://github.com/anansi-project/comicinfo/blob/main/schema/v2.0/ComicInfo.xsd.
    private static string? MapAgeRating(ContentRating rating) => rating switch
    {
        ContentRating.Safe => "Everyone",
        ContentRating.Suggestive => "Teen",
        ContentRating.Erotica => "Mature 17+",
        ContentRating.Pornographic => "X18+",
        _ => null,
    };
}
