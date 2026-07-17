using System.Text.Json.Serialization;

namespace MangaFusion.Sources.ComicVine.Dtos;

/// <summary>ComicVine wraps every response in the same envelope. <see cref="StatusCode"/> is 1 on
/// success; anything else (100 = invalid API key, 101 = object not found) carries a message in
/// <see cref="Error"/> and an empty/absent <see cref="Results"/>, and comes back over HTTP 200 —
/// so the status code in the body is the only reliable signal of failure.</summary>
internal sealed class ComicVineResponseDto<T>
{
    [JsonPropertyName("status_code")]
    public int StatusCode { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("number_of_total_results")]
    public int NumberOfTotalResults { get; set; }

    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }

    [JsonPropertyName("results")]
    public T? Results { get; set; }
}

internal sealed class ComicVineImageDto
{
    [JsonPropertyName("medium_url")]
    public string? MediumUrl { get; set; }

    [JsonPropertyName("original_url")]
    public string? OriginalUrl { get; set; }
}

/// <summary>A named reference to another ComicVine resource — the shape every volume credit list uses
/// (people, characters, concepts) and also the publisher.
///
/// <see cref="Count"/> is how many issues of the volume the entity appears in, and it arrives as a
/// <b>string</b>, not a number. It's the only signal available for ranking: a volume can credit
/// hundreds of characters, and count is what separates the leads from a one-panel cameo.</summary>
internal sealed class ComicVineRefDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("count")]
    public string? Count { get; set; }
}

/// <summary>A ComicVine volume — the closest analogue to a series (e.g. "Batman (2011)").</summary>
internal sealed class ComicVineVolumeDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Newline-separated alternate titles, not a JSON array.</summary>
    [JsonPropertyName("aliases")]
    public string? Aliases { get; set; }

    /// <summary>Full description — HTML, must be stripped before display.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Short plain-text summary. Preferred over <see cref="Description"/> when present.</summary>
    [JsonPropertyName("deck")]
    public string? Deck { get; set; }

    /// <summary>A year, but delivered as a string (and occasionally blank or non-numeric).</summary>
    [JsonPropertyName("start_year")]
    public string? StartYear { get; set; }

    [JsonPropertyName("count_of_issues")]
    public int? CountOfIssues { get; set; }

    /// <summary>The volume's page on comicvine.gamespot.com. Its slug can't be reconstructed from the id,
    /// so it has to be carried through rather than built client-side.</summary>
    [JsonPropertyName("site_detail_url")]
    public string? SiteDetailUrl { get; set; }

    [JsonPropertyName("image")]
    public ComicVineImageDto? Image { get; set; }

    [JsonPropertyName("publisher")]
    public ComicVineRefDto? Publisher { get; set; }

    // The volume resource names its credit lists "people"/"characters"/"concepts". The *_credits names
    // (person_credits, character_credits, …) belong to the *issue* resource — asking a volume for them
    // is not an error, it just silently returns nothing, which is exactly how this was mis-modelled at
    // first. A volume has no teams and no story arcs at all.
    [JsonPropertyName("people")]
    public List<ComicVineRefDto>? People { get; set; }

    [JsonPropertyName("characters")]
    public List<ComicVineRefDto>? Characters { get; set; }

    [JsonPropertyName("concepts")]
    public List<ComicVineRefDto>? Concepts { get; set; }
}

/// <summary>A ComicVine issue — the closest analogue to a chapter.</summary>
internal sealed class ComicVineIssueDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Usually numeric ("12"), but can be "Annual 1" or "½" — never assume it parses.</summary>
    [JsonPropertyName("issue_number")]
    public string? IssueNumber { get; set; }

    [JsonPropertyName("cover_date")]
    public string? CoverDate { get; set; }

    [JsonPropertyName("store_date")]
    public string? StoreDate { get; set; }
}
