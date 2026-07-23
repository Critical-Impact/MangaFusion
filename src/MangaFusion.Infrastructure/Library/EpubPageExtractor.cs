using System.IO.Compression;
using System.Xml.Linq;
using MangaFusion.Infrastructure.Reading;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Reads pages out of an image-based/fixed-layout "comic" EPUB — where every spine item is a
/// single full-page image, either directly or wrapped in a thin XHTML/SVG shell — so it can be
/// imported through the same page-file pipeline as a CBZ/folder/PDF source. Deliberately rejects
/// anything that looks like a genuine reflowable-text EPUB (a novel) rather than attempting a
/// best-effort render: this app is a paginated image reader with no text-reading UI, so there is
/// nothing sensible to do with real prose content. Mirrors <see cref="PdfPageExtractor"/>'s shape.
/// </summary>
public sealed class EpubPageExtractor
{
    /// <summary>How much non-whitespace body text a spine page may carry alongside its one image before
    /// it's treated as reflowable content rather than a comic page — comic-EPUB generators routinely
    /// emit incidental strings (a page-number caption, a title attribute) even on genuine image pages,
    /// so a strict "zero text" rule would misfire on real comics.</summary>
    private const int MaxIncidentalTextLength = 60;

    public int CountPages(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return ResolveSpinePages(zip).Count;
    }

    public async Task<List<string>> ExtractPagesAsync(string path, string destDir, CancellationToken ct)
    {
        Directory.CreateDirectory(destDir);

        using var zip = ZipFile.OpenRead(path);
        var pages = ResolveSpinePages(zip);

        var results = new List<string>();
        var index = 0;
        foreach (var imageEntryName in pages)
        {
            ct.ThrowIfCancellationRequested();
            var entry = zip.GetEntry(imageEntryName)
                ?? throw new InvalidOperationException($"EPUB references a missing file '{imageEntryName}'.");
            var dest = Path.Combine(destDir, $"{(index + 1):D5}{Path.GetExtension(imageEntryName)}");
            await using (var entryStream = entry.Open())
            await using (var fileStream = File.Create(dest))
            {
                await entryStream.CopyToAsync(fileStream, ct);
            }

            results.Add(dest);
            index++;
        }

        return results;
    }

    /// <summary>Walks container.xml -> OPF manifest/spine and resolves each spine item, in reading
    /// order, to the single full-page image entry it represents. Throws if the EPUB is DRM-encrypted
    /// (image bytes would be garbage) or if any spine item doesn't cleanly resolve to exactly one image
    /// with no more than incidental surrounding text — i.e. this isn't an image-based comic EPUB.
    /// </summary>
    private static List<string> ResolveSpinePages(ZipArchive zip)
    {
        if (zip.GetEntry("META-INF/encryption.xml") is not null)
        {
            throw new InvalidOperationException("This EPUB is DRM-protected and can't be imported.");
        }

        var opfPath = FindOpfPath(zip);
        var opfDir = ZipDirectoryOf(opfPath);
        var opf = LoadXml(zip, opfPath);
        var ns = opf.Root!.Name.Namespace;

        var manifestElement = opf.Root.Element(ns + "manifest")
            ?? throw new InvalidOperationException("EPUB package document has no <manifest>.");
        var manifest = manifestElement.Elements(ns + "item")
            .Where(e => (string?)e.Attribute("id") is not null && (string?)e.Attribute("href") is not null)
            .ToDictionary(
                e => (string)e.Attribute("id")!,
                e => (
                    Href: ResolveZipPath(opfDir, (string)e.Attribute("href")!),
                    MediaType: (string?)e.Attribute("media-type") ?? string.Empty));

        var spineElement = opf.Root.Element(ns + "spine")
            ?? throw new InvalidOperationException("EPUB package document has no <spine>.");
        var spine = spineElement.Elements(ns + "itemref")
            .Select(e => (string?)e.Attribute("idref"))
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();

        if (spine.Count == 0)
        {
            throw new InvalidOperationException("EPUB spine is empty.");
        }

        var pages = new List<string>(spine.Count);
        foreach (var idref in spine)
        {
            if (!manifest.TryGetValue(idref, out var item))
            {
                throw new InvalidOperationException($"EPUB spine references unknown manifest item '{idref}'.");
            }

            pages.Add(ResolveSpinePage(zip, item.Href, item.MediaType));
        }

        return pages;
    }

    private static string ResolveSpinePage(ZipArchive zip, string href, string mediaType)
    {
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) && ImageContentType.IsImage(href))
        {
            return href;
        }

        if (!mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            && !mediaType.Equals("application/svg+xml", StringComparison.OrdinalIgnoreCase))
        {
            throw NotImageBased(href);
        }

        var doc = LoadXml(zip, href);
        var baseDir = ZipDirectoryOf(href);
        var body = doc.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("body", StringComparison.OrdinalIgnoreCase))
            ?? doc.Root;

        var images = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var textLength = 0;

        if (body is not null)
        {
            foreach (var el in body.DescendantsAndSelf())
            {
                var local = el.Name.LocalName.ToLowerInvariant();
                if (local is "style" or "script")
                {
                    continue;
                }

                if (local is "img" or "image")
                {
                    var src = el.Attributes()
                        .FirstOrDefault(a =>
                            a.Name.LocalName.Equals("src", StringComparison.OrdinalIgnoreCase)
                            || a.Name.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase))
                        ?.Value;
                    if (!string.IsNullOrWhiteSpace(src))
                    {
                        images.Add(ResolveZipPath(baseDir, src));
                    }
                }
                else if (!el.HasElements)
                {
                    textLength += el.Value.Trim().Length;
                }
            }
        }

        if (images.Count != 1 || textLength > MaxIncidentalTextLength)
        {
            throw NotImageBased(href);
        }

        return images.Single();
    }

    private static InvalidOperationException NotImageBased(string itemPath) => new(
        $"This EPUB contains reflowable text content ('{itemPath}') and isn't supported — only " +
        "image-based/fixed-layout comic EPUBs can be imported.");

    private static string FindOpfPath(ZipArchive zip)
    {
        var container = LoadXml(zip, "META-INF/container.xml");
        var ns = container.Root!.Name.Namespace;
        var fullPath = container.Root.Element(ns + "rootfiles")?.Elements(ns + "rootfile")
            .Select(e => (string?)e.Attribute("full-path"))
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        return fullPath ?? throw new InvalidOperationException("EPUB container.xml has no rootfile.");
    }

    private static XDocument LoadXml(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidOperationException($"EPUB is missing '{entryName}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string ZipDirectoryOf(string zipPath)
    {
        var slash = zipPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : zipPath[..slash];
    }

    /// <summary>Resolves an EPUB-internal relative href against the zip-entry path of the document it
    /// came from, normalizing "./"/"../" segments (zip entries always use forward slashes regardless of
    /// OS) and URI-decoding percent-escapes and stripping any "#fragment".</summary>
    private static string ResolveZipPath(string baseDir, string href)
    {
        var decoded = Uri.UnescapeDataString(href.Split('#')[0]);
        var combined = string.IsNullOrEmpty(baseDir) ? decoded : $"{baseDir}/{decoded}";

        var parts = new List<string>();
        foreach (var segment in combined.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }

                continue;
            }

            parts.Add(segment);
        }

        return string.Join('/', parts);
    }
}
