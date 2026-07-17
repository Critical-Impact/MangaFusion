namespace MangaFusion.Domain.Library;

/// <summary>Shared right-to-left detection for a series' original language, plus the known-language
/// table backing language pickers/validation across the app (default language, follow/series
/// auto-download languages). Kept as validated strings rather than a literal enum since sources
/// (MangaDex) can report codes — regional variants like "es-la"/"pt-br"/"zh-hk" — that a
/// hand-maintained enum would need constant updates to track; this mirrors how UI themes are
/// validated (see Program.cs's knownThemeIds).</summary>
public static class MangaLanguage
{
    public static bool IsRightToLeft(string? originalLanguage)
    {
        var lang = originalLanguage?.ToLowerInvariant();
        return lang is not null && (lang == "ja" || lang.StartsWith("zh"));
    }

    /// <summary>MangaDex's supported translated-language codes + English display names.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> KnownLanguages =
    [
        ("af", "Afrikaans"),
        ("sq", "Albanian"),
        ("ar", "Arabic"),
        ("az", "Azerbaijani"),
        ("eu", "Basque"),
        ("bn", "Bengali"),
        ("bg", "Bulgarian"),
        ("my", "Burmese"),
        ("ca", "Catalan"),
        ("zh", "Chinese (Simplified)"),
        ("zh-hk", "Chinese (Traditional)"),
        ("hr", "Croatian"),
        ("cs", "Czech"),
        ("da", "Danish"),
        ("nl", "Dutch"),
        ("en", "English"),
        ("eo", "Esperanto"),
        ("et", "Estonian"),
        ("tl", "Filipino"),
        ("fi", "Finnish"),
        ("fr", "French"),
        ("ka", "Georgian"),
        ("de", "German"),
        ("el", "Greek"),
        ("he", "Hebrew"),
        ("hi", "Hindi"),
        ("hu", "Hungarian"),
        ("id", "Indonesian"),
        ("ga", "Irish"),
        ("it", "Italian"),
        ("ja", "Japanese"),
        ("kk", "Kazakh"),
        ("ko", "Korean"),
        ("lt", "Lithuanian"),
        ("ms", "Malay"),
        ("mn", "Mongolian"),
        ("ne", "Nepali"),
        ("no", "Norwegian"),
        ("fa", "Persian"),
        ("pl", "Polish"),
        ("pt", "Portuguese"),
        ("pt-br", "Portuguese (Brazil)"),
        ("ro", "Romanian"),
        ("ru", "Russian"),
        ("sr", "Serbian"),
        ("sk", "Slovak"),
        ("sl", "Slovenian"),
        ("es", "Spanish"),
        ("es-la", "Spanish (LATAM)"),
        ("sv", "Swedish"),
        ("ta", "Tamil"),
        ("te", "Telugu"),
        ("th", "Thai"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("ur", "Urdu"),
        ("vi", "Vietnamese"),
    ];

    private static readonly HashSet<string> KnownCodes =
        new(KnownLanguages.Select(l => l.Code), StringComparer.OrdinalIgnoreCase);

    public static bool IsKnown(string? code) => code is not null && KnownCodes.Contains(code);

    public static string? TryGetName(string code) =>
        KnownLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)).Name;
}
