using System.Globalization;
using System.Text.RegularExpressions;

namespace MangaFusion.Sources.Web.Util;

/// <summary>Derives a numeric chapter number from a chapter's display title — a pragmatic port of
/// Tachiyomi's <c>ChapterRecognition</c>. Web sources rarely expose a clean chapter number, so the
/// number is recovered from text like "Chapter 12.5". Returns the number, or the supplied fallback
/// (default <c>-1</c>) when nothing recognisable is found.</summary>
public static partial class ChapterRecognition
{
    [GeneratedRegex(@"(?:chapter|chap|ch|episode|epis|ep)\.?\s*([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeywordNumber();

    [GeneratedRegex(@"[0-9]+(?:\.[0-9]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex AnyNumber();

    public static float Parse(string? chapterName, float fallback = -1f)
    {
        if (string.IsNullOrWhiteSpace(chapterName)) return fallback;

        // Prefer a number that directly follows a chapter keyword ("Chapter 12", "Ch. 12.5").
        var keyword = KeywordNumber().Match(chapterName);
        if (keyword.Success && TryFloat(keyword.Groups[1].Value, out var kn)) return kn;

        // Otherwise fall back to the last number in the title (skips leading volume/season numbers).
        var numbers = AnyNumber().Matches(chapterName);
        if (numbers.Count > 0 && TryFloat(numbers[^1].Value, out var last)) return last;

        return fallback;
    }

    private static bool TryFloat(string s, out float value) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
