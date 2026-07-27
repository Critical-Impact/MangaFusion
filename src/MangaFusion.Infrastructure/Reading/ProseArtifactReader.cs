using System.Buffers.Text;
using System.IO.Compression;
using System.Text;
using AngleSharp.Html.Parser;
using MangaFusion.Application.Reading;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Reads a stored EPUB3 prose artifact for the text reader. A prose artifact is one whole volume
/// = one chapter, so the entire spine is read in reading order and concatenated into a single sanitized
/// document — cover page, prose sections, and full-page illustration plates interleaved exactly as the
/// source EPUB has them. This is what lets an imported light-novel EPUB be stored untouched and still
/// render faithfully (the deliberate counterpart to <see cref="EpubPageExtractor"/>, which reads
/// image-page comic EPUBs). Inline images are addressed by a stable, self-describing name that
/// round-trips to the image's zip-entry path, so they can be served statelessly.</summary>
public sealed class ProseArtifactReader : IProseArtifactReader
{
    public async Task<ProseChapterContent?> ReadBookAsync(string absolutePath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(absolutePath);
        var spine = EpubZipPaths.ResolveSpine(zip);
        var parser = new HtmlParser();

        var sections = new List<string>();
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var wordCount = 0;

        foreach (var item in spine)
        {
            ct.ThrowIfCancellationRequested();
            if (!IsTextDocument(item.MediaType))
            {
                continue;
            }

            var entry = zip.GetEntry(item.Href);
            if (entry is null)
            {
                continue;
            }

            string rawHtml;
            await using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            {
                rawHtml = await reader.ReadToEndAsync(ct);
            }

            var baseDir = EpubZipPaths.ZipDirectoryOf(item.Href);
            var result = ProseHtmlSanitizer.Sanitize(parser, rawHtml, baseDir, zipPath => ResolveImage(zip, zipPath));

            if (!string.IsNullOrWhiteSpace(result.Html))
            {
                sections.Add(result.Html);
            }

            foreach (var (name, type) in result.Images)
            {
                images[name] = type;
            }

            wordCount += result.WordCount;
        }

        if (sections.Count == 0 && images.Count == 0)
        {
            return null;
        }

        // Each spine section is wrapped in a marked <section> so the reader has stable, image-load-proof
        // anchors to track/restore reading position against (a whole-volume EPUB is one long scroll, and a
        // raw scroll fraction drifts as images below load in — anchoring to sections fixes that).
        var html = string.Concat(sections.Select(s => $"<section class=\"prose-section\">{s}</section>"));
        return new ProseChapterContent(html, images, wordCount);
    }

    public async Task<PageContent?> OpenImageAsync(
        string absolutePath, string imageName, CancellationToken ct = default)
    {
        var zipPath = DecodeImageName(imageName);
        if (zipPath is null || !ImageContentType.IsImage(zipPath))
        {
            return null;
        }

        using var zip = ZipFile.OpenRead(absolutePath);
        var entry = zip.GetEntry(zipPath);
        if (entry is null)
        {
            return null;
        }

        var capacity = entry.Length is > 0 and < int.MaxValue ? (int)entry.Length : 0;
        var buffer = new MemoryStream(capacity);
        await using (var source = entry.Open())
        {
            await source.CopyToAsync(buffer, ct);
        }

        buffer.Position = 0;
        return new PageContent(buffer, ImageContentType.ForName(zipPath), buffer.Length);
    }

    /// <summary>Maps a resolved zip-entry path to a stable image ref, or null if it isn't a real image
    /// entry in this EPUB (so the sanitizer drops a dangling/non-image <c>&lt;img&gt;</c>).</summary>
    private static ProseImageRef? ResolveImage(ZipArchive zip, string zipPath)
    {
        if (!ImageContentType.IsImage(zipPath) || zip.GetEntry(zipPath) is null)
        {
            return null;
        }

        return new ProseImageRef(EncodeImageName(zipPath), ImageContentType.ForName(zipPath));
    }

    private static bool IsTextDocument(string mediaType) =>
        mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase);

    // The image name is the Base64Url of its zip-entry path: self-describing (no per-request re-parse of
    // the book to map names back to entries) and URL-path-safe (no '/', '+', '=' or fragment chars).
    private static string EncodeImageName(string zipPath) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(zipPath));

    private static string? DecodeImageName(string name)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64Url.DecodeFromChars(name));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
