using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.ComicVine.Dtos;

namespace MangaFusion.Sources.ComicVine.Mapping;

/// <summary>Maps ComicVine's volume/issue shape onto the provider-neutral contracts.
///
/// Three mismatches, all verified against the live API rather than the docs:
/// <list type="bullet">
/// <item>ComicVine has no genre/theme vocabulary. A volume instead credits a publisher, characters and
/// concepts (no teams, no story arcs — those exist only on issues), so those become the comic library's
/// filter facets.</item>
/// <item>A volume's people carry <b>no role</b> — the writer/artist distinction only exists on the issue
/// resource. So everyone is mapped as an author ("creator"); artists is deliberately left empty rather
/// than guessed at.</item>
/// <item>There is no publication-status and no content-rating field. Inferring "completed" from the issue
/// count would be wrong for any ongoing series between arcs, so both stay Unknown.</item>
/// </list></summary>
internal static partial class ComicVineMapper
{
    public const string PublisherGroup = "publisher";
    public const string CharacterGroup = "character";
    public const string ConceptGroup = "concept";

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTag();

    /// <summary>ComicVine descriptions embed images as &lt;figure&gt; blocks whose caption text is editorial
    /// chrome, not prose — stripping tags alone leaves The Sandman's description opening with the words
    /// "House Ad". The whole block goes.</summary>
    [GeneratedRegex("<figure.*?</figure>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HtmlFigure();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static SourceSeries ToSeries(ComicVineVolumeDto dto)
    {
        var tagRefs = BuildTagRefs(dto);

        // Everyone credited on the volume is a "creator". ComicVine only attaches roles to issue credits,
        // and fetching every issue just to label someone a penciler would blow the 200-requests/hour
        // budget for one series. Ranked by appearance count so the principals lead.
        var creators = ByCount(dto.People)
            .Select(p => new SourceAuthorRef(p.Id.ToString(CultureInfo.InvariantCulture), p.Name!.Trim()))
            .ToList();

        return new SourceSeries
        {
            SourceId = ComicVineConstants.SourceId,
            SourceSeriesId = dto.Id.ToString(CultureInfo.InvariantCulture),
            Title = string.IsNullOrWhiteSpace(dto.Name) ? "(untitled volume)" : dto.Name.Trim(),
            AltTitles = ParseAliases(dto.Aliases),

            // `description` is the real prose (as HTML — must be stripped). `deck` is only a fallback: it's
            // frequently a useless stub (the Sandman volume's deck is literally "Volume 2.").
            Description = StripHtml(dto.Description)
                ?? (string.IsNullOrWhiteSpace(dto.Deck) ? null : dto.Deck.Trim()),

            CoverUrl = dto.Image?.MediumUrl ?? dto.Image?.OriginalUrl,

            Authors = creators.Select(p => p.Name).ToList(),
            AuthorRefs = creators,

            // Left empty on purpose — see the class remarks; a volume exposes no roles to split on.
            Artists = [],
            ArtistRefs = [],

            Tags = tagRefs.Select(t => t.Name).ToList(),
            TagRefs = tagRefs,

            // ComicVine carries no rating and no status.
            ContentRating = ContentRating.Unknown,
            Status = PublicationStatus.Unknown,

            Year = ParseYear(dto.StartYear),
            OriginalLanguage = "en",
            AvailableTranslatedLanguages = ["en"],

            // count_of_issues is a real count, not a last-issue number, so it belongs in ChapterCount.
            // Comics are remade constantly under the same title, so the start year and the issue count are
            // often the only things that tell two "Batman" volumes apart — both feed the import matcher.
            ChapterCount = dto.CountOfIssues,
            LastChapter = null,
            SiteUrl = dto.SiteDetailUrl,
        };
    }

    public static SourceChapter ToChapter(ComicVineIssueDto dto) => new()
    {
        SourceId = ComicVineConstants.SourceId,
        SourceChapterId = dto.Id.ToString(CultureInfo.InvariantCulture),
        Number = string.IsNullOrWhiteSpace(dto.IssueNumber) ? null : dto.IssueNumber.Trim(),
        Title = string.IsNullOrWhiteSpace(dto.Name) ? null : dto.Name.Trim(),
        Language = "en",

        // Comics have no scanlation groups — an issue has exactly one canonical release.
        ScanlationGroups = [],

        // store_date is the real on-sale date; cover_date is the (often months-ahead) printed date, so
        // it's only a fallback.
        PublishedAt = ParseDate(dto.StoreDate) ?? ParseDate(dto.CoverDate),

        // ComicVine serves no pages. The issue exists so local files can be matched against it and given
        // real numbers/titles; the artifact itself always comes from an import.
        IsExternal = false,
    };

    /// <summary>Publisher, characters and concepts become tags — that's the whole of a comic's filterable
    /// metadata, standing in for the genre/theme facets a manga source would provide.</summary>
    private static List<SourceTagRef> BuildTagRefs(ComicVineVolumeDto dto)
    {
        var refs = new List<SourceTagRef>();

        if (dto.Publisher is { } publisher && !string.IsNullOrWhiteSpace(publisher.Name))
        {
            refs.Add(new SourceTagRef(TagId(PublisherGroup, publisher.Id), publisher.Name.Trim(), PublisherGroup));
        }

        Add(dto.Characters, CharacterGroup);
        Add(dto.Concepts, ConceptGroup);
        return refs;

        void Add(List<ComicVineRefDto>? credits, string group)
        {
            foreach (var credit in ByCount(credits))
            {
                refs.Add(new SourceTagRef(TagId(group, credit.Id), credit.Name!.Trim(), group));
            }
        }
    }

    /// <summary>The most significant credits first, capped. A long-running volume credits hundreds of
    /// characters (The Sandman: 196), the overwhelming majority of them one-panel cameos — taking them all
    /// would flood the Tag table and make the filter dropdown useless. ComicVine's <c>count</c> (issues the
    /// entity appears in) is the only ranking signal it offers, and it's a good one: it puts Dream and Death
    /// at the top of Sandman rather than whoever happened to be first in the array.</summary>
    private static IEnumerable<ComicVineRefDto> ByCount(List<ComicVineRefDto>? credits) =>
        (credits ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .OrderByDescending(ParseCount)
            .Take(ComicVineConstants.MaxCreditsPerGroup);

    /// <summary><c>count</c> is delivered as a string, and is absent on some entities.</summary>
    private static int ParseCount(ComicVineRefDto credit) =>
        int.TryParse(credit.Count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;

    /// <summary>ComicVine ids are only unique within a resource type — a character and a concept can both
    /// be id 42 — so the group is folded into the tag id that gets persisted as <c>Tag.SourceTagId</c>.</summary>
    private static string TagId(string group, int id) =>
        $"{group}:{id.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Aliases arrive as one newline-separated string, not a JSON array.</summary>
    private static List<string> ParseAliases(string? aliases) =>
        string.IsNullOrWhiteSpace(aliases)
            ? []
            : aliases.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

    private static int? ParseYear(string? startYear) =>
        int.TryParse(startYear?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? new DateTimeOffset(parsed.ToUniversalTime(), TimeSpan.Zero)
            : null;

    /// <summary>ComicVine descriptions are HTML. Strip the tags and decode entities so the text is usable
    /// anywhere — the reader, the series page, an import wizard's match preview.</summary>
    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = WebUtility.HtmlDecode(HtmlTag().Replace(HtmlFigure().Replace(html, " "), " "));
        text = Whitespace().Replace(text, " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
