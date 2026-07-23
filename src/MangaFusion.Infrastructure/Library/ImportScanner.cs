using System.Text.RegularExpressions;

namespace MangaFusion.Infrastructure.Library;

/// <summary>One importable file discovered under the import inbox, with its release-name-parsed
/// title/volume guess. The guess is never authoritative — it only seeds the review UI's search box
/// and chapter-number fields, both fully editable before commit. <see cref="FileName"/> is relative to
/// the release folder (<see cref="FolderName"/>) — may include subfolder segments, since a release's
/// actual chapter files/folders can sit one or more levels below the top-level release folder.</summary>
public sealed record ScannedImportFile(
    string FolderName, string FileName, string FullPath, ChapterSourceKind Kind,
    string ParsedTitle, string? ParsedVolume, int PageCount, long SizeBytes,
    /// <summary>The issue/chapter number parsed from the name ("100 Bullets #017" → "17"), when the name
    /// carries one. Comics are distributed one file per <em>issue</em>, where manga releases are one file
    /// per <em>volume</em> — so this is what a comic import fills its chapter number from.</summary>
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
    // shorter "v03"/"V3" form common on individual volume-scan filenames.
    private static readonly Regex VolumePattern =
        new(@"\bv(?:ol(?:ume)?)?\.?\s*0*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    public IReadOnlyList<ScannedImportGroup> ScanInbox(string inboxRoot)
    {
        if (!Directory.Exists(inboxRoot))
        {
            return [];
        }

        var files = new List<ScannedImportFile>();
        foreach (var dir in Directory.EnumerateDirectories(inboxRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            files.AddRange(ScanReleaseFolder(dir));
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
    private List<ScannedImportFile> ScanReleaseFolder(string dir)
    {
        var folderName = Path.GetFileName(dir);
        var (title, folderVolume, folderIssue) = ParseFolderName(folderName);
        var results = new List<ScannedImportFile>();

        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var kind = ChapterSourceKindClassifier.FromFileName(file);
            if (kind is null)
            {
                continue;
            }

            int pages;
            try
            {
                pages = chapterImporter.CountPages(file, kind.Value);
            }
            catch (InvalidOperationException)
            {
                // e.g. an EPUB that turns out to be reflowable text rather than an image-based comic,
                // or a corrupt archive — not importable, so skip it rather than failing the whole scan.
                continue;
            }

            if (pages == 0)
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file);
            var volume = ParseVolume(stem) ?? folderVolume;
            results.Add(new ScannedImportFile(
                folderName, Path.GetRelativePath(dir, file), file, kind.Value, title, volume, pages,
                new FileInfo(file).Length, ParseIssue(stem) ?? folderIssue));
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
            var volume = ParseVolume(name) ?? folderVolume;
            results.Add(new ScannedImportFile(
                folderName, relative, candidate, ChapterSourceKind.Folder, title, volume, folderPages, size,
                ParseIssue(name) ?? folderIssue));
        }

        return results;
    }

    private static string? ParseVolume(string text)
    {
        var match = VolumePattern.Match(text);
        return match.Success ? match.Groups[1].Value : null;
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

        var titleTokens = s
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !NoiseTokens.Contains(t) && !YearTokenPattern.IsMatch(t));

        var title = string.Join(' ', titleTokens).Trim();
        return (title.Length > 0 ? title : folderName, volume, issue);
    }
}
