using System.Globalization;
using MangaFusion.Domain.Library;

namespace MangaFusion.Application.Library;

/// <summary>Normalizes source chapter numbers into a sortable value and a dedup key. The key is what
/// collapses different groups' releases of "the same chapter" into one logical chapter.</summary>
public static class ChapterNumber
{
    /// <summary><paramref name="volume"/> only matters when <paramref name="number"/> is blank: a blank
    /// number with a volume set means "this file is an entire volume, not one numbered chapter" (a
    /// manual/import-only concept — chapter-feed sources like MangaDex always supply a number). In that
    /// case the key/sort are derived from the volume instead of collapsing to a single shared "oneshot"
    /// key, so e.g. whole-volume 1 and whole-volume 2 of the same series don't collide and still sort in
    /// volume order.
    ///
    /// <paramref name="title"/> is a last-resort discriminator, used only when both number and volume are
    /// blank: scraped sources often expose a named chapter ("Prologue", "Extra", "Side Story") whose title
    /// carries no recognizable number, and every such chapter would otherwise collapse onto the single
    /// shared "oneshot" key — silently merging distinct chapters into one logical chapter. Keying by title
    /// keeps them apart while still collapsing same-titled (or title-less) oneshot releases across groups.
    /// Both number and volume blank with no title is a true oneshot.</summary>
    public static (decimal? Sort, string Key) Normalize(string? number, string? volume = null, string? title = null)
    {
        var trimmedNumber = number?.Trim();
        if (!string.IsNullOrEmpty(trimmedNumber))
        {
            if (decimal.TryParse(trimmedNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                // "0.####" strips trailing zeros so "10", "10.0" and "10.00" share a key.
                return (value, value.ToString("0.####", CultureInfo.InvariantCulture));
            }

            return (null, trimmedNumber.ToLowerInvariant());
        }

        var trimmedVolume = volume?.Trim();
        if (!string.IsNullOrEmpty(trimmedVolume))
        {
            if (decimal.TryParse(trimmedVolume, NumberStyles.Number, CultureInfo.InvariantCulture, out var volValue))
            {
                return (volValue, $"vol-{volValue.ToString("0.####", CultureInfo.InvariantCulture)}");
            }

            return (null, $"vol-{trimmedVolume.ToLowerInvariant()}");
        }

        var trimmedTitle = title?.Trim();
        if (!string.IsNullOrEmpty(trimmedTitle))
        {
            return (null, $"title-{trimmedTitle.ToLowerInvariant()}");
        }

        return (null, "oneshot");
    }

    /// <summary>Normalized primary scanlation-group name used for preference matching (null = no group).</summary>
    public static string? GroupKey(IReadOnlyList<string> groups) =>
        groups.Count > 0 && !string.IsNullOrWhiteSpace(groups[0])
            ? groups[0].Trim().ToLowerInvariant()
            : null;

    /// <summary>Sortable numeric form of a raw volume string (null when unparseable/absent) — the
    /// <see cref="Chapter.VolumeSort"/> companion to <see cref="Normalize"/>'s own Sort, used when a
    /// series' <see cref="ChapterSortMode"/> is <see cref="ChapterSortMode.VolumeThenChapter"/>.</summary>
    public static decimal? VolumeSort(string? volume)
    {
        var trimmed = volume?.Trim();
        return !string.IsNullOrEmpty(trimmed) &&
            decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Volume-qualifies an identity <paramref name="key"/> (from <see cref="Normalize"/>) for
    /// series using <see cref="ChapterSortMode.VolumeThenChapter"/> — a no-op for the default
    /// <see cref="ChapterSortMode.Absolute"/> mode. Qualification is needed because <c>NumberKey</c> is
    /// not just a sort tiebreak, it's the DB's dedup identity: without it, two different volumes'
    /// same-numbered "extra" chapters (e.g. Vol.7 Ch.1 and Vol.8 Ch.1) would collapse onto one Chapter
    /// row. The volume part is normalized the same way <see cref="Normalize"/>'s own volume branch is,
    /// so e.g. "7" and "07" share a namespace instead of being treated as different volumes.</summary>
    public static string QualifyKey(ChapterSortMode mode, string key, string? volume)
    {
        if (mode == ChapterSortMode.Absolute) return key;

        var trimmedVolume = volume?.Trim();
        string volumePart;
        if (string.IsNullOrEmpty(trimmedVolume))
        {
            volumePart = "novol";
        }
        else if (decimal.TryParse(trimmedVolume, NumberStyles.Number, CultureInfo.InvariantCulture, out var volValue))
        {
            volumePart = volValue.ToString("0.####", CultureInfo.InvariantCulture);
        }
        else
        {
            volumePart = trimmedVolume.ToLowerInvariant();
        }

        return $"{volumePart}:{key}";
    }
}
