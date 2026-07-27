using System.IO.Compression;
using AngleSharp.Html.Parser;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Decides whether an EPUB is a reflowable-text novel (→ prose pipeline) or an image-based /
/// fixed-layout comic (→ image pipeline). A comic EPUB's spine documents are near-empty of body text
/// (just full-page images, maybe a caption); a novel's carry real paragraphs. So the discriminator is
/// simply how much body text the spine holds. Used at import time so a light-novel library can accept
/// both a scanned volume and a text novel and route each correctly.</summary>
internal static class EpubContentClassifier
{
    /// <summary>Total non-whitespace body-text characters across spine documents above which the EPUB is
    /// treated as prose. Comfortably above the incidental text a comic page carries, well below a single
    /// prose page.</summary>
    private const int ProseTextThreshold = 500;

    public static bool IsProse(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var spine = EpubZipPaths.ResolveSpine(zip);
        var parser = new HtmlParser();
        var textChars = 0;

        foreach (var item in spine)
        {
            if (!IsTextDocument(item.MediaType))
            {
                continue;
            }

            var entry = zip.GetEntry(item.Href);
            if (entry is null)
            {
                continue;
            }

            string raw;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
            {
                raw = reader.ReadToEnd();
            }

            var body = parser.ParseDocument(raw).Body;
            if (body is null)
            {
                continue;
            }

            textChars += body.TextContent.Count(c => !char.IsWhiteSpace(c));
            if (textChars >= ProseTextThreshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTextDocument(string mediaType) =>
        mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase);
}
