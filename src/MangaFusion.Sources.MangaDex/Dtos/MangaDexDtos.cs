namespace MangaFusion.Sources.MangaDex.Dtos;

// Minimal DTOs for the MangaDex JSON we consume. Deserialized case-insensitively (MangaDex is
// camelCase). Localized fields are language -> text dictionaries.

internal sealed class MangaListDto
{
    public List<MangaDataDto> Data { get; set; } = [];
    public int Limit { get; set; }
    public int Offset { get; set; }
    public int Total { get; set; }
}

internal sealed class MangaEntityDto
{
    public MangaDataDto? Data { get; set; }
}

internal sealed class MangaDataDto
{
    public string Id { get; set; } = "";
    public MangaAttributesDto Attributes { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = [];
}

internal sealed class MangaAttributesDto
{
    public Dictionary<string, string>? Title { get; set; }
    public List<Dictionary<string, string>>? AltTitles { get; set; }
    public Dictionary<string, string>? Description { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Status { get; set; }
    public int? Year { get; set; }
    public string? ContentRating { get; set; }
    public string? LastChapter { get; set; }
    public List<TagDto>? Tags { get; set; }
    public List<string>? AvailableTranslatedLanguages { get; set; }
}

internal sealed class TagDto
{
    public string Id { get; set; } = "";
    public TagAttributesDto Attributes { get; set; } = new();
}

internal sealed class TagAttributesDto
{
    public Dictionary<string, string>? Name { get; set; }
    public string? Group { get; set; }
}

internal sealed class RelationshipDto
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public RelationshipAttributesDto? Attributes { get; set; }
}

internal sealed class RelationshipAttributesDto
{
    public string? FileName { get; set; } // cover_art
    public string? Name { get; set; }     // author / artist / scanlation_group
}

internal sealed class ChapterListDto
{
    public List<ChapterDataDto> Data { get; set; } = [];
    public int Limit { get; set; }
    public int Offset { get; set; }
    public int Total { get; set; }
}

internal sealed class ChapterDataDto
{
    public string Id { get; set; } = "";
    public ChapterAttributesDto Attributes { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = [];
}

internal sealed class ChapterAttributesDto
{
    public string? Volume { get; set; }
    public string? Chapter { get; set; }
    public string? Title { get; set; }
    public string? TranslatedLanguage { get; set; }
    public string? ExternalUrl { get; set; }
    public int? Pages { get; set; }
    public DateTimeOffset? PublishAt { get; set; }
}

internal sealed class TagListDto
{
    public List<TagEntityDto> Data { get; set; } = [];
}

internal sealed class TagEntityDto
{
    public string Id { get; set; } = "";
    public TagEntityAttributesDto Attributes { get; set; } = new();
}

internal sealed class TagEntityAttributesDto
{
    public Dictionary<string, string>? Name { get; set; }
    public string? Group { get; set; }
}

internal sealed class AtHomeDto
{
    public string BaseUrl { get; set; } = "";
    public AtHomeChapterDto Chapter { get; set; } = new();
}

internal sealed class AtHomeChapterDto
{
    public string Hash { get; set; } = "";
    public List<string> Data { get; set; } = [];
    public List<string> DataSaver { get; set; } = [];
}
