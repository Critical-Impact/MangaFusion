using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using Microsoft.Extensions.Logging;

namespace MangaFusion.Infrastructure.Library;

/// <summary>One CBZ/folder file discovered under a migration series' inbox folder, with its
/// ComicInfo and filename facts resolved. Pure local I/O — no network calls, no DB.</summary>
public sealed record ScannedFile(
    string FileName,
    string FullPath,
    StorageFormat Format,
    string? UuidPrefix,
    string? Number,
    string NumberKey,
    string? ChapterTitle,
    string? ComicInfoSeriesTitle,
    int PageCount,
    long SizeBytes,
    string? IntegrityFailureReason);

/// <summary>One inbox subfolder awaiting migration (one series' worth of chapter files).</summary>
public sealed record ScannedSeriesFolder(string FolderName, string FullPath, IReadOnlyList<ScannedFile> Files);

/// <summary><paramref name="Folders"/> are normal migration candidates (at least one file carried a
/// readable ComicInfo.xml). <paramref name="FoldersWithNoComicInfo"/> contain chapter-shaped files
/// (.cbz/subfolder) but none of them had one at all — almost certainly not from the old MangaDex
/// downloader this tool targets, so the caller should redirect them elsewhere rather than treating
/// them as (unmatchable) migration candidates. A folder with no chapter-shaped entries at all lands
/// in neither list — there's nothing there to act on.</summary>
public sealed record MigrationScanResult(
    IReadOnlyList<ScannedSeriesFolder> Folders, IReadOnlyList<string> FoldersWithNoComicInfo);

/// <summary>Reads the old downloader's CBZ/folder layout: one subfolder per series, one file per
/// chapter, each carrying a ComicInfo.xml and a filename ending in <c>_&lt;8-hex-uuid-prefix&gt;</c>
/// (the first segment of the MangaDex chapter id — see the old tool's <c>GetDownloadFolder</c>).</summary>
public sealed class MigrationScanner(ArtifactFileInspector artifactInspector, ILogger<MigrationScanner> logger)
{
    // Below this, a CBZ is almost certainly a pageless metadata-only stub (external-chapter
    // placeholder) rather than a real chapter — verified against the sample data.
    private const long MinSizeBytes = 10 * 1024;

    private static readonly Regex UuidPrefixPattern =
        new(@"_([0-9a-f]{8})(?:\.[^.\\/]+)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The old downloader wrote raw "&" into text fields (e.g. Notes: "Gal & Dino") instead of
    // escaping it as "&amp;", producing invalid XML. Re-escape any "&" not already part of a
    // recognized entity/character reference before parsing.
    private static readonly Regex BareAmpersandPattern =
        new(@"&(?!amp;|lt;|gt;|quot;|apos;|#\d+;|#x[0-9a-fA-F]+;)", RegexOptions.Compiled);

    public MigrationScanResult ScanInbox(string inboxRoot)
    {
        if (!Directory.Exists(inboxRoot))
        {
            return new MigrationScanResult([], []);
        }

        var folders = new List<ScannedSeriesFolder>();
        var noComicInfo = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(inboxRoot).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var files = ScanSeriesFolder(dir);
            if (files.Count > 0)
            {
                folders.Add(new ScannedSeriesFolder(Path.GetFileName(dir), dir, files));
            }
            else if (HasAnyChapterCandidate(dir))
            {
                noComicInfo.Add(dir);
            }
        }

        return new MigrationScanResult(folders, noComicInfo);
    }

    /// <summary>Whether <paramref name="dir"/> has anything that could plausibly be a chapter file
    /// (a .cbz or a subfolder) — independent of whether it carries a readable ComicInfo.xml.</summary>
    private static bool HasAnyChapterCandidate(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Any(e =>
            Directory.Exists(e) || (File.Exists(e) && e.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase)));

    public List<ScannedFile> ScanSeriesFolder(string dir)
    {
        var results = new List<ScannedFile>();

        foreach (var entry in Directory.EnumerateFileSystemEntries(dir).OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
        {
            var isCbz = File.Exists(entry) && entry.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase);
            var isFolder = Directory.Exists(entry);
            if (!isCbz && !isFolder)
            {
                continue;
            }

            var format = isCbz ? StorageFormat.Cbz : StorageFormat.Folder;
            var (series, number, title) = ReadComicInfo(entry, format);
            if (series is null && number is null && title is null)
            {
                continue; // no ComicInfo — not a chapter file the migration tool understands
            }

            var fileName = Path.GetFileName(entry)!;
            var pages = artifactInspector.CountPages(entry, format);
            var size = isCbz ? new FileInfo(entry).Length : artifactInspector.DirectorySize(entry);
            var key = ChapterNumber.Normalize(number).Key;

            var integrityReason = pages == 0
                ? "No image pages — likely a metadata-only external-chapter stub."
                : size < MinSizeBytes
                    ? $"Suspiciously small ({size} bytes) for a chapter file."
                    : null;

            results.Add(new ScannedFile(
                fileName, entry, format,
                ExtractUuidPrefix(fileName),
                number, key, title, series,
                pages, size,
                integrityReason));
        }

        return results;
    }

    /// <summary>Pulls the trailing 8-hex-char UUID-prefix segment off a filename (or folder name),
    /// e.g. <c>Chapter25_[EN-data]_Title_f60bac7d.cbz</c> → <c>f60bac7d</c>. Null if absent.</summary>
    public static string? ExtractUuidPrefix(string fileName)
    {
        var match = UuidPrefixPattern.Match(fileName);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private (string? Series, string? Number, string? Title) ReadComicInfo(string path, StorageFormat format)
    {
        try
        {
            using var stream = OpenComicInfo(path, format);
            if (stream is null)
            {
                return (null, null, null);
            }

            string xml;
            using (var reader = new StreamReader(stream))
            {
                xml = reader.ReadToEnd();
            }

            var sanitized = BareAmpersandPattern.Replace(xml, "&amp;");
            var root = XDocument.Parse(sanitized).Root;
            if (root is null)
            {
                return (null, null, null);
            }

            string? Get(string name) => (string?)root.Element(name) is { Length: > 0 } v ? v.Trim() : null;
            return (Get("Series"), Get("Number"), Get("Title"));
        }
        catch (Exception ex)
        {
            // Unreadable/corrupt archive or malformed ComicInfo — treat as "not a chapter file"
            // rather than failing the whole scan over one bad entry, but log it: silently
            // skipping a file is otherwise indistinguishable from "not a chapter file".
            logger.LogWarning(ex, "Failed to read ComicInfo.xml from {Path} — treating as not a chapter file.", path);
            return (null, null, null);
        }
    }

    private static Stream? OpenComicInfo(string path, StorageFormat format)
    {
        if (format == StorageFormat.Folder)
        {
            var file = Path.Combine(path, "ComicInfo.xml");
            return File.Exists(file) ? File.OpenRead(file) : null;
        }

        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(
            e => string.Equals(e.Name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        // Buffer into memory — the ZipArchive (and its entry stream) is disposed on return.
        var buffer = new MemoryStream();
        using (var entryStream = entry.Open())
        {
            entryStream.CopyTo(buffer);
        }

        buffer.Position = 0;
        return buffer;
    }
}
