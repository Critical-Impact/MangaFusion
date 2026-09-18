using System.Text.RegularExpressions;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Library;

/// <summary>One importable file discovered under the import inbox, with its release-name-parsed
/// title/volume guess. The guess is never authoritative — it only seeds the review UI's search box
/// and chapter-number fields, both fully editable before commit. <see cref="FileName"/> is relative to
/// the release folder (<see cref="FolderName"/>) — may include subfolder segments, since a release's
/// actual chapter files/folders can sit one or more levels below the top-level release folder.</summary>
public sealed record ScannedImportFile(
    string FolderName, string FileName, string FullPath, ChapterSourceKind Kind,
    string ParsedTitle, string? ParsedVolume, int PageCount, long SizeBytes,
    /// <summary>The chapter number parsed from the name, when it carries one: a comic's issue
    /// ("100 Bullets #017" → "17"), or a manga's explicit chapter marker ("Name Ch.5" → "5"). A manga file
    /// without one is a whole volume, since manga releases are usually one file per <em>volume</em>.</summary>
    string? ParsedNumber = null);

/// <summary>Inbox folders whose parsed titles normalize to the same value, grouped into one candidate
/// series (e.g. two volumes of the same release become one group with two files).</summary>
public sealed record ScannedImportGroup(string GroupTitle, IReadOnlyList<ScannedImportFile> Files);

/// <summary>Scans the import wizard's inbox: one subfolder per release (matching how digital volume
/// purchases are typically organized — publisher/scene-style folder names, no ComicInfo.xml). Parses a
/// best-effort title + volume number from each folder name and groups same-title folders together.
/// Pure local I/O — no network calls, no DB.</summary>
public sealed class ImportScanner(ChapterFileImporter chapterImporter)
{
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "Manga", "Comic", "Comics", "eBook", "Ebook", "Hybrid", "Digital", "Retail", "Scan", "Repack",
    };

    // Matches "Vol.3"/"Vol 3"/"Volume 3" (the original scene-release convention) as well as the
    // shorter "v03"/"V3" form common on individual volume-scan filenames. Letter lookarounds rather than
    // \b so an underscore-separated "Name_v01_ch003" still matches.
    private static readonly Regex VolumePattern =
        new(@"(?<![a-z])v(?:ol(?:ume)?)?\.?\s*0*(\d+)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "Ch.1"/"Ch 001"/"Chapter 12.5" — an explicit manga chapter marker. A range ("Ch.1-5") is a
    // multi-chapter file with no single number, so ParseChapter declines it rather than guessing its first
    // chapter — but it still counts as a chapter marker, so the file isn't mistaken for a volume.
    private static readonly Regex ChapterPattern = new(
        @"(?<![a-z])ch(?:ap(?:ter)?)?\.?\s*0*(\d+(?:\.\d+)?)(\s*[-–~]\s*\d+(?:\.\d+)?)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "#017" — the unambiguous comic issue marker, and the only one safe to trust anywhere in the name.
    private static readonly Regex HashIssuePattern =
        new(@"#\s*0*(\d+(?:\.\d+)?)", RegexOptions.Compiled);

    // A bare trailing number: "100 Bullets 017", "Batman 001 (2000)", "Watchmen 12 (of 12)". It has to be
    // anchored to the *end* (ignoring trailing parenthetical/bracketed junk like a year, "(of 12)", or a
    // scanner tag), because a number anywhere else is usually part of the title — "100 Bullets" would
    // otherwise import as issue 100 of a series called "Bullets".
    private static readonly Regex TrailingIssuePattern = new(
        @"(?:^|[\s._-])0*(\d{1,4}(?:\.\d+)?)\s*(?:(?:\([^)]*\)|\[[^\]]*\]|\{[^}]*\})\s*)*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingPublisherPattern = new(@"^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*-", RegexOptions.Compiled);
    private static readonly Regex TrailingGroupPattern = new(@"-[A-Za-z0-9]+$", RegexOptions.Compiled);
    private static readonly Regex YearTokenPattern = new(@"^(19|20)\d{2}$", RegexOptions.Compiled);

    public IReadOnlyList<ScannedImportGroup> ScanInbox(string inboxRoot, MediaKind kind)
    {
        if (!Directory.Exists(inboxRoot))
        {
            return [];
        }

        var files = new List<ScannedImportFile>();
        foreach (var dir in Directory.EnumerateDirectories(inboxRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            files.AddRange(ScanReleaseFolder(dir, kind));
        }

        // A single file dropped directly in the inbox root (common for light novels — one EPUB per volume,
        // no release subfolder) is its own release, keyed off the file name rather than a folder name.
        foreach (var file in Directory.EnumerateFiles(inboxRoot).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (ScanLooseFile(file, kind) is { } loose)
            {
                files.Add(loose);
            }
        }

        return files
            .GroupBy(f => GroupKey(f))
            .Select(g => new ScannedImportGroup(
                g.OrderBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase).First().ParsedTitle,
                g.OrderBy(f => f.FolderName, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(g => g.GroupTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string GroupKey(ScannedImportFile f)
    {
        var normalized = TitleMatching.Normalize(f.ParsedTitle);
        // An empty parse (e.g. a folder name that's entirely noise tokens) must not silently merge
        // unrelated folders into one bucket — fall back to a per-folder key.
        return normalized.Length > 0 ? normalized : $"\0{f.FolderName}";
    }

    /// <summary>One release folder: the folder name alone determines the series/title guess (never
    /// any subfolder's name), but the actual chapter files are searched for recursively underneath it
    /// — releases are sometimes organized with the CBZ/PDF (or page-image folder) one or more levels
    /// below the top-level release folder rather than directly inside it. Every CBZ/PDF found anywhere
    /// under the folder becomes its own item; only if none are found anywhere does the scanner fall
    /// back to treating some directory under it (the release folder itself, or a subfolder) that
    /// directly contains page images as a single folder-of-images item. Volume is guessed per file
    /// first (a batch folder can contain several volumes' worth of files, each named "v03" etc.),
    /// falling back to the release folder name's guess when the individual file/subfolder name carries
    /// no volume marker of its own.</summary>
    private List<ScannedImportFile> ScanReleaseFolder(string dir, MediaKind kind)
    {
        var folderName = Path.GetFileName(dir);
        var (title, folderVolume, folderIssue) = ParseFolderName(folderName);
        var folderNumber = ParseNumber(folderName, folderIssue, kind);
        var results = new List<ScannedImportFile>();

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (ClassifyFile(file, kind) is not (var sourceKind, var pages))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file);
            var (volume, number) = ParseItemNumbers(stem, title, kind, folderVolume, folderNumber);
            results.Add(new ScannedImportFile(
                folderName, Path.GetRelativePath(dir, file), file, sourceKind, title, volume, pages,
                new FileInfo(file).Length, number));
        }

        if (results.Count > 0)
        {
            return results;
        }

        // No CBZ/PDF anywhere under this release — check whether the release folder itself, or some
        // subfolder of it, is directly a folder of page images.
        var candidates = new[] { dir }.Concat(Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var folderPages = chapterImporter.CountPages(candidate, ChapterSourceKind.Folder);
            if (folderPages == 0)
            {
                continue;
            }

            var size = Directory.EnumerateFiles(candidate).Sum(f => new FileInfo(f).Length);
            var relative = candidate == dir ? "" : Path.GetRelativePath(dir, candidate);
            var name = Path.GetFileName(candidate);
            var (volume, number) = ParseItemNumbers(name, title, kind, folderVolume, folderNumber);
            results.Add(new ScannedImportFile(
                folderName, relative, candidate, ChapterSourceKind.Folder, title, volume, folderPages, size,
                number));
        }

        return results;
    }

    /// <summary>Classifies one file for the batch's library and resolves its page count, or null if it
    /// isn't importable. Prose files (light-novel text/EPUB/PDF) carry no page count — they import as a
    /// whole-volume chapter — so they report 0 and are still included; image files must yield a positive
    /// page count (a reflowable EPUB or corrupt archive throws and is skipped).</summary>
    private (ChapterSourceKind Kind, int PageCount)? ClassifyFile(string file, MediaKind kind)
    {
        var sourceKind = ChapterSourceKindClassifier.ClassifyForKind(file, kind);
        if (sourceKind is null)
        {
            return null;
        }

        if (ChapterSourceKindClassifier.IsProse(sourceKind.Value))
        {
            return (sourceKind.Value, 0);
        }

        try
        {
            var pages = chapterImporter.CountPages(file, sourceKind.Value);
            return pages == 0 ? null : (sourceKind.Value, pages);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>A single importable file sitting directly in the inbox root (no release subfolder). Its
    /// own name seeds the title/volume guess and it commits from the inbox root (empty folder name).</summary>
    private ScannedImportFile? ScanLooseFile(string file, MediaKind kind)
    {
        if (ClassifyFile(file, kind) is not (var sourceKind, var pages))
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(file);
        var (title, volume, issue) = ParseFolderName(stem);
        return new ScannedImportFile(
            "", Path.GetFileName(file), file, sourceKind, title, volume, pages,
            new FileInfo(file).Length, ParseNumber(stem, issue, kind));
    }

    /// <summary>The chapter number a release name carries for this library: a comic's issue number, a
    /// manga's explicit "Ch." marker, and nothing for light novels (always whole-volume imports).</summary>
    private static string? ParseNumber(string name, string? parsedIssue, MediaKind kind) => kind switch
    {
        MediaKind.Comic => parsedIssue,
        MediaKind.Manga => ParseChapter(name),
        _ => null,
    };

    /// <summary>Volume/number for one file (or image folder) inside a release folder, falling back to the
    /// release folder's own guesses. Manga and light novels additionally treat a bare trailing number
    /// ("Name 001") as the volume, since both ship one file per volume — but only as the weakest signal,
    /// and never when the name is just the series title itself ("Mob Psycho 100" is not volume 100).</summary>
    private static (string? Volume, string? Number) ParseItemNumbers(
        string name, string seriesTitle, MediaKind kind, string? folderVolume, string? folderNumber)
    {
        var volume = ParseVolume(name) ?? folderVolume;
        if (kind is MediaKind.Manga or MediaKind.LightNovel
            && volume is null && !ChapterPattern.IsMatch(name) && !IsSeriesTitle(name, seriesTitle))
        {
            volume = ParseIssue(name);
        }

        return kind switch
        {
            MediaKind.Comic => (volume, ParseIssue(name) ?? folderNumber),
            MediaKind.Manga => (volume, ParseChapter(name) ?? folderNumber),
            _ => (volume, null),
        };
    }

    private static bool IsSeriesTitle(string name, string seriesTitle)
    {
        return Alphanumerics(name) == Alphanumerics(seriesTitle);

        static string Alphanumerics(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static string? ParseVolume(string text)
    {
        var match = VolumePattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    public static string? ParseChapter(string text)
    {
        var match = ChapterPattern.Match(text);
        return match.Success && !match.Groups[2].Success ? match.Groups[1].Value : null;
    }

    /// <summary>The issue number from a comic filename. "#017" wins wherever it appears; otherwise a bare
    /// number is only trusted at the very end of the name. Returns null when the name carries a volume
    /// marker instead ("Saga v01"), since that's a collected edition, not an issue — and null for a plain
    /// year, so "Batman (2016)" doesn't become issue 2016.</summary>
    public static string? ParseIssue(string text)
    {
        var hash = HashIssuePattern.Match(text);
        if (hash.Success)
        {
            return Trim(hash.Groups[1].Value);
        }

        // "Saga v01" is a trade paperback — a volume, not an issue. Don't guess a number out of it.
        if (VolumePattern.IsMatch(text))
        {
            return null;
        }

        var trailing = TrailingIssuePattern.Match(text);
        if (!trailing.Success)
        {
            return null;
        }

        var value = trailing.Groups[1].Value;

        // A trailing 4-digit year is a publication date, not an issue number.
        if (YearTokenPattern.IsMatch(value))
        {
            return null;
        }

        return Trim(value);

        // Strip the leading zeros the regex already skipped past, but keep a bare "0" (issue #0 exists).
        static string Trim(string raw) => raw.Length == 0 ? "0" : raw;
    }

    /// <summary>Best-effort scene-release-name parse: strips a leading publisher prefix and trailing
    /// release-group suffix, pulls out a volume and/or issue number, and drops common noise tokens
    /// (format/year/edition tags). Never authoritative — only seeds the review UI.</summary>
    public static (string Title, string? Volume, string? Issue) ParseFolderName(string folderName)
    {
        var s = folderName;

        var groupMatch = TrailingGroupPattern.Match(s);
        if (groupMatch.Success)
        {
            s = s[..groupMatch.Index];
        }

        var pubMatch = LeadingPublisherPattern.Match(s);
        if (pubMatch.Success)
        {
            s = s[pubMatch.Length..];
        }

        var issue = ParseIssue(s);

        string? volume = null;
        var volMatch = VolumePattern.Match(s);
        if (volMatch.Success)
        {
            volume = volMatch.Groups[1].Value;
            s = string.Concat(s.AsSpan(0, volMatch.Index), s.AsSpan(volMatch.Index + volMatch.Length));
        }

        // A "#17" is never part of a series' name, so drop it from the title — otherwise a folder-per-issue
        // layout would scatter "100 Bullets #017" and "#018" into separate one-file series.
        s = HashIssuePattern.Replace(s, " ");
        s = ChapterPattern.Replace(s, " ");

        var titleTokens = s
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !NoiseTokens.Contains(t) && !YearTokenPattern.IsMatch(t));

        // Trim separator punctuation left dangling at the ends once a volume/issue token is pulled out —
        // light-novel names use a " - Volume NN" dash separator (not the scene-release "." convention), so
        // removing the volume leaves a trailing " -" that would otherwise ride along into the title.
        var title = string.Join(' ', titleTokens).Trim().Trim('-', '_', '–', ' ').Trim();
        return (title.Length > 0 ? title : folderName, volume, issue);
    }
}
