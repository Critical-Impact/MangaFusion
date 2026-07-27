using System.IO.Compression;
using System.Xml.Linq;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Shared EPUB container/OPF plumbing: locating the package document, loading XML entries,
/// resolving EPUB-internal relative hrefs against the zip, and reading the spine in reading order.
/// Hoisted out of <see cref="EpubPageExtractor"/> so both it (comic image EPUBs) and
/// <c>ProseArtifactReader</c>/<c>EpubContentClassifier</c> (reflowable-text EPUBs) resolve zip paths and
/// the spine the same way — a mechanical shared helper, not new behaviour.</summary>
internal static class EpubZipPaths
{
    /// <summary>One manifest item that a spine <c>itemref</c> resolved to, in reading order:
    /// <paramref name="Href"/> is the zip-entry path, <paramref name="MediaType"/> its declared type.</summary>
    internal readonly record struct SpineItem(string Href, string MediaType);

    /// <summary>Walks container.xml → OPF manifest/spine and returns each spine item, in reading order,
    /// as its resolved zip-entry href + media type. Throws if the EPUB is DRM-encrypted or structurally
    /// broken (missing manifest/spine, empty spine, dangling idref). What each item <em>means</em> (a
    /// page image vs a prose document) is the caller's decision.</summary>
    public static List<SpineItem> ResolveSpine(ZipArchive zip)
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
                e => new SpineItem(
                    ResolveZipPath(opfDir, (string)e.Attribute("href")!),
                    (string?)e.Attribute("media-type") ?? string.Empty));

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

        var items = new List<SpineItem>(spine.Count);
        foreach (var idref in spine)
        {
            if (!manifest.TryGetValue(idref, out var item))
            {
                throw new InvalidOperationException($"EPUB spine references unknown manifest item '{idref}'.");
            }

            items.Add(item);
        }

        return items;
    }

    public static string FindOpfPath(ZipArchive zip)
    {
        var container = LoadXml(zip, "META-INF/container.xml");
        var ns = container.Root!.Name.Namespace;
        var fullPath = container.Root.Element(ns + "rootfiles")?.Elements(ns + "rootfile")
            .Select(e => (string?)e.Attribute("full-path"))
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        return fullPath ?? throw new InvalidOperationException("EPUB container.xml has no rootfile.");
    }

    public static XDocument LoadXml(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName)
            ?? throw new InvalidOperationException($"EPUB is missing '{entryName}'.");
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    public static string ZipDirectoryOf(string zipPath)
    {
        var slash = zipPath.LastIndexOf('/');
        return slash < 0 ? string.Empty : zipPath[..slash];
    }

    /// <summary>Resolves an EPUB-internal relative href against the zip-entry path of the document it
    /// came from, normalizing "./"/"../" segments (zip entries always use forward slashes regardless of
    /// OS), URI-decoding percent-escapes and stripping any "#fragment".</summary>
    public static string ResolveZipPath(string baseDir, string href)
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
