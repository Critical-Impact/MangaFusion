using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using AngleSharp;
using AngleSharp.Html.Parser;
using AngleSharp.Xhtml;
using MangaFusion.Application.Writing;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Reading;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Writes a minimal, valid EPUB3 (one file per prose artifact): <c>mimetype</c>,
/// <c>META-INF/container.xml</c>, an OPF package document (manifest + spine + Dublin Core metadata), a
/// real <c>nav.xhtml</c> TOC, one XHTML per chapter segment, and inline images under
/// <c>OEBPS/images/</c>. Series/author/genre metadata goes into the OPF's native Dublin Core block — the
/// container that already has the right slot for it — not a bolted-on ComicInfo.xml. Portable to
/// Komga/Kavita/Calibre, which open EPUB as a novel for free. The prose counterpart to
/// <see cref="CbzChapterWriter"/>, and it borrows its temp-file-then-move crash-safety and
/// <see cref="LibraryPaths.UniquePath"/> collision handling verbatim.</summary>
public sealed class EpubChapterWriter : IProseChapterWriter
{
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Xhtml = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";
    private static readonly XNamespace Container = "urn:oasis:names:tc:opendocument:xmlns:container";

    public async Task<ProseWriteResult> WriteAsync(ProseWriteRequest request, CancellationToken ct = default)
    {
        if (request.Segments.Count == 0)
        {
            throw new InvalidOperationException("A prose write needs at least one chapter segment.");
        }

        Directory.CreateDirectory(request.TargetDirectory);
        var path = LibraryPaths.UniquePath(Path.Combine(request.TargetDirectory, request.FileBaseName + ".epub"));
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var file = File.Create(tempPath))
            using (var zip = new ZipArchive(file, ZipArchiveMode.Create))
            {
                // EPUB OCF: the mimetype entry must be first and stored uncompressed.
                var mimetype = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
                await using (var s = mimetype.Open())
                {
                    var bytes = Encoding.ASCII.GetBytes("application/epub+zip");
                    await s.WriteAsync(bytes, ct);
                }

                await WriteTextEntryAsync(zip, "META-INF/container.xml", ContainerXml(), ct);

                var chapters = new List<ChapterFile>();
                var parser = new HtmlParser();
                for (var i = 0; i < request.Segments.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var segment = request.Segments[i];
                    var chapterFile = $"chapter{i + 1:D4}.xhtml";
                    var imageDir = $"images/ch{i + 1:D4}";

                    var (bodyXhtml, images) = await BuildChapterBodyAsync(zip, parser, segment, imageDir, ct);
                    var title = segment.Title ?? ChapterHeading(segment);
                    await WriteTextEntryAsync(zip, $"OEBPS/{chapterFile}",
                        ChapterXhtml(title, segment.Language, bodyXhtml), ct);

                    chapters.Add(new ChapterFile(chapterFile, title, images));
                }

                await WriteTextEntryAsync(zip, "OEBPS/nav.xhtml",
                    NavXhtml(request.SeriesTitle, request.Segments[0].Language, chapters), ct);
                await WriteTextEntryAsync(zip, "OEBPS/content.opf", ContentOpf(request, chapters), ct);
            }

            var (hash, size) = await HashAsync(tempPath, ct);
            File.Move(tempPath, path, overwrite: false);
            return new ProseWriteResult(path, size, request.Segments.Count, hash);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed record ChapterFile(string Href, string Title, IReadOnlyList<ImageFile> Images);

    private sealed record ImageFile(string Id, string Href, string MediaType);

    /// <summary>Copies a segment's inline images into <paramref name="imageDir"/>, rewrites each
    /// <c>&lt;img src="{name}"&gt;</c> in the body to point at the embedded copy, and returns the body as
    /// well-formed XHTML plus the manifest entries for the images it actually used.</summary>
    private static async Task<(string BodyXhtml, List<ImageFile> Images)> BuildChapterBodyAsync(
        ZipArchive zip, HtmlParser parser, ProseChapterSegment segment, string imageDir, CancellationToken ct)
    {
        var doc = parser.ParseDocument($"<body>{segment.Html}</body>");
        var body = doc.Body!;
        var images = new List<ImageFile>();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal); // name -> rewritten href

        foreach (var img in body.QuerySelectorAll("img"))
        {
            var name = img.GetAttribute("src");
            if (string.IsNullOrEmpty(name) || !segment.Images.TryGetValue(name, out var sourcePath))
            {
                img.Remove();
                continue;
            }

            if (!seen.TryGetValue(name, out var href))
            {
                var safeName = LibraryPaths.Sanitize(name);
                href = $"{imageDir}/{safeName}";
                await CopyImageEntryAsync(zip, $"OEBPS/{href}", sourcePath, ct);
                images.Add(new ImageFile($"img-{images.Count + 1}-{imageDir.Replace('/', '-')}", href,
                    ImageContentType.ForName(safeName)));
                seen[name] = href;
            }

            img.SetAttribute("src", href);
        }

        var bodyXhtml = string.Concat(body.ChildNodes.Select(n => n.ToHtml(XhtmlMarkupFormatter.Instance)));
        return (bodyXhtml, images);
    }

    private static async Task CopyImageEntryAsync(ZipArchive zip, string entryName, string sourcePath, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        await using var target = entry.Open();
        await using var source = File.OpenRead(sourcePath);
        await source.CopyToAsync(target, ct);
    }

    private static async Task WriteTextEntryAsync(ZipArchive zip, string entryName, string content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes, ct);
    }

    private static string ContainerXml() =>
        Declaration(new XElement(Container + "container",
            new XAttribute("version", "1.0"),
            new XElement(Container + "rootfiles",
                new XElement(Container + "rootfile",
                    new XAttribute("full-path", "OEBPS/content.opf"),
                    new XAttribute("media-type", "application/oebps-package+xml")))));

    private static string ContentOpf(ProseWriteRequest request, List<ChapterFile> chapters)
    {
        var lang = request.Segments[0].Language;
        var subjects = request.Genres.Concat(request.OtherTags ?? []).Where(s => !string.IsNullOrWhiteSpace(s));

        var metadata = new XElement(Opf + "metadata",
            new XAttribute(XNamespace.Xmlns + "dc", Dc.NamespaceName),
            new XElement(Dc + "identifier", new XAttribute("id", "book-id"), $"urn:uuid:{Guid.NewGuid()}"),
            new XElement(Dc + "title", request.SeriesTitle),
            new XElement(Dc + "language", string.IsNullOrWhiteSpace(lang) ? "en" : lang),
            new XElement(Opf + "meta",
                new XAttribute("property", "dcterms:modified"),
                DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")));

        foreach (var author in request.Authors.Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            metadata.Add(new XElement(Dc + "creator", author));
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            metadata.Add(new XElement(Dc + "description", request.Description));
        }

        foreach (var subject in subjects)
        {
            metadata.Add(new XElement(Dc + "subject", subject));
        }

        var manifest = new XElement(Opf + "manifest",
            new XElement(Opf + "item",
                new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"),
                new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")));

        var spine = new XElement(Opf + "spine");

        for (var i = 0; i < chapters.Count; i++)
        {
            var id = $"chapter{i + 1}";
            manifest.Add(new XElement(Opf + "item",
                new XAttribute("id", id), new XAttribute("href", chapters[i].Href),
                new XAttribute("media-type", "application/xhtml+xml")));
            spine.Add(new XElement(Opf + "itemref", new XAttribute("idref", id)));

            foreach (var image in chapters[i].Images)
            {
                manifest.Add(new XElement(Opf + "item",
                    new XAttribute("id", image.Id), new XAttribute("href", image.Href),
                    new XAttribute("media-type", image.MediaType)));
            }
        }

        var package = new XElement(Opf + "package",
            new XAttribute("version", "3.0"),
            new XAttribute("unique-identifier", "book-id"),
            new XAttribute(XNamespace.Xml + "lang", string.IsNullOrWhiteSpace(lang) ? "en" : lang),
            metadata, manifest, spine);

        return Declaration(package);
    }

    private static string NavXhtml(string title, string lang, List<ChapterFile> chapters)
    {
        var items = chapters.Select((c, i) =>
            new XElement(Xhtml + "li",
                new XElement(Xhtml + "a", new XAttribute("href", c.Href), c.Title)));

        var html = new XElement(Xhtml + "html",
            new XAttribute(XNamespace.Xmlns + "epub", Epub.NamespaceName),
            new XAttribute(XNamespace.Xml + "lang", Lang(lang)),
            new XElement(Xhtml + "head",
                new XElement(Xhtml + "meta", new XAttribute("charset", "utf-8")),
                new XElement(Xhtml + "title", title)),
            new XElement(Xhtml + "body",
                new XElement(Xhtml + "nav",
                    new XAttribute(Epub + "type", "toc"),
                    new XAttribute("id", "toc"),
                    new XElement(Xhtml + "h1", "Contents"),
                    new XElement(Xhtml + "ol", items))));

        return XhtmlDocument(html);
    }

    /// <summary>The per-chapter XHTML. The body content is already well-formed XHTML (serialized by
    /// AngleSharp's XHTML formatter), so it's injected as a raw XML fragment inside the template.</summary>
    private static string ChapterXhtml(string title, string lang, string bodyXhtml)
    {
        var l = Lang(lang);
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            $"<html xmlns=\"{Xhtml.NamespaceName}\" xml:lang=\"{l}\" lang=\"{l}\">\n" +
            $"<head><meta charset=\"utf-8\"/><title>{System.Security.SecurityElement.Escape(title)}</title></head>\n" +
            $"<body>{bodyXhtml}</body>\n</html>\n";
    }

    private static string ChapterHeading(ProseChapterSegment segment) =>
        segment.Number is not null ? $"Chapter {segment.Number}"
        : segment.Volume is not null ? $"Volume {segment.Volume}"
        : "Chapter";

    private static string Lang(string? lang) => string.IsNullOrWhiteSpace(lang) ? "en" : lang;

    private static string Declaration(XElement root) =>
        new XDeclaration("1.0", "utf-8", null) + Environment.NewLine + root;

    private static string XhtmlDocument(XElement html) =>
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + html;

    private static async Task<(string Hash, long Size)> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return (Convert.ToHexStringLower(hash), stream.Length);
    }
}
