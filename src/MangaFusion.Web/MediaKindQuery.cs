using MangaFusion.Domain.Library;

namespace MangaFusion.Web;

/// <summary>Parses the <c>?kind=</c> query parameter that scopes every library-facing endpoint to one
/// half of the app. Absent or unrecognised means manga, which keeps existing callers working and makes
/// a typo'd kind fall back to the default library rather than 400 on a read.</summary>
public static class MediaKindQuery
{
    public static MediaKind Parse(string? value) =>
        Enum.TryParse<MediaKind>(value, ignoreCase: true, out var parsed) ? parsed : MediaKind.Manga;

    /// <summary>Like <see cref="Parse"/>, but an absent <c>?kind=</c> means "both libraries" rather than
    /// defaulting to manga. Only the Home rails use this: they're the one place a user can opt into a
    /// combined view (<c>ApplicationUser.HomeAcrossLibraries</c>), and there the client omits the param.</summary>
    public static MediaKind? ParseOptional(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.TryParse<MediaKind>(value, ignoreCase: true, out var parsed) ? parsed : MediaKind.Manga;
}
