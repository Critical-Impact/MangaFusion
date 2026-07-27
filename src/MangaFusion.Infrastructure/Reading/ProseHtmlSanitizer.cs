using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>A surviving inline image: the stable <paramref name="Name"/> its <c>&lt;img src&gt;</c> is
/// rewritten to (which the endpoint turns into an image URL) and its <paramref name="ContentType"/>.</summary>
internal readonly record struct ProseImageRef(string Name, string ContentType);

/// <summary>Server-side sanitizer for one prose chapter's XHTML body, run before the HTML ever reaches the
/// client (which renders it via <c>innerHTML</c>). This is the security boundary — never rely on the
/// frontend to sanitize what it's about to inject. Drops scripting/foreign/form elements
/// (<c>&lt;script&gt;/&lt;style&gt;/&lt;svg&gt;/&lt;math&gt;/&lt;form&gt;/…</c>), strips event-handler and
/// <c>style</c> attributes, and removes any URL-bearing attribute (<c>href</c>, <c>xlink:href</c>,
/// <c>formaction</c>) whose value isn't a plain http(s) link — that covers dangerous pseudo-schemes
/// (<c>javascript:</c>, <c>data:</c>) and intra-EPUB relative links, which would otherwise drive the SPA's
/// hash router to a bogus route. Surviving <c>&lt;img&gt;</c>s are rewritten to the stable image name the
/// caller resolves — dropping images that don't resolve to a real image entry in the EPUB.</summary>
internal static class ProseHtmlSanitizer
{
    public sealed record Result(string Html, IReadOnlyDictionary<string, string> Images, int WordCount);

    // URL-bearing attributes scrubbed to http(s)-only on every surviving element (img src is handled
    // separately by RewriteImage, which needs the raw relative value to resolve the zip entry).
    private static readonly string[] UrlAttributes = ["href", "xlink:href", "formaction"];

    /// <param name="parser">Shared parser reused across a volume's spine sections — a whole-volume read
    /// parses dozens of sections, so the caller owns one instance rather than allocating per section.</param>
    /// <param name="resolveImage">Maps an image's resolved zip-entry path to its stable name +
    /// content type, or null to drop the image (not a real image entry in this EPUB).</param>
    public static Result Sanitize(
        HtmlParser parser, string rawHtml, string baseDir, Func<string, ProseImageRef?> resolveImage)
    {
        var doc = parser.ParseDocument(rawHtml);
        var body = doc.Body;
        if (body is null)
        {
            return new Result(string.Empty, new Dictionary<string, string>(), 0);
        }

        var images = new Dictionary<string, string>(StringComparer.Ordinal);

        // Snapshot first: the tree is mutated (elements removed/replaced) as we go.
        foreach (var el in body.QuerySelectorAll("*").ToList())
        {
            if (!IsWithin(el, body))
            {
                continue; // already removed as a descendant of an earlier drop
            }

            var tag = el.LocalName.ToLowerInvariant();

            if (tag is "script" or "style" or "iframe" or "object" or "embed" or "link" or "meta" or "base"
                or "svg" or "math" or "form" or "button" or "input" or "textarea" or "select")
            {
                el.Remove();
                continue;
            }

            var isImage = tag is "img" or "image";

            // Strip event handlers, inline styles, srcset (a second image-URL vector), and any URL-bearing
            // attribute that isn't a plain http(s) link — javascript:/data: pseudo-schemes and intra-EPUB
            // relative links alike. Image URLs are left for RewriteImage, which rebuilds a bare safe src.
            foreach (var attr in el.Attributes.Select(a => a.Name).ToList())
            {
                if (attr.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                    || attr.Equals("style", StringComparison.OrdinalIgnoreCase)
                    || attr.Equals("srcset", StringComparison.OrdinalIgnoreCase)
                    || (!isImage && IsUrlAttribute(attr) && !IsHttpUrl(el.GetAttribute(attr))))
                {
                    el.RemoveAttribute(attr);
                }
            }

            if (isImage)
            {
                RewriteImage(el, baseDir, resolveImage, images);
            }
        }

        var wordCount = body.TextContent
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;

        return new Result(body.InnerHtml, images, wordCount);
    }

    /// <summary>Whether <paramref name="el"/> is still attached under <paramref name="root"/> — false once
    /// it (or an ancestor) has been removed from the tree, so we skip re-processing dropped descendants.</summary>
    private static bool IsWithin(INode el, INode root)
    {
        for (INode? node = el; node is not null; node = node.Parent)
        {
            if (node == root)
            {
                return true;
            }
        }

        return false;
    }

    private static void RewriteImage(
        IElement img, string baseDir, Func<string, ProseImageRef?> resolveImage, Dictionary<string, string> images)
    {
        var src = img.GetAttribute("src") ?? img.GetAttribute("href")
            ?? img.GetAttribute("xlink:href");
        if (string.IsNullOrWhiteSpace(src) || IsExternal(src))
        {
            img.Remove();
            return;
        }

        var resolved = resolveImage(EpubZipPaths.ResolveZipPath(baseDir, src));
        if (resolved is not { } imageRef)
        {
            img.Remove();
            return;
        }

        // Reduce every image to a bare, safe <img src="{name}"> — no xlink, no dimensions-driven layout.
        var alt = img.GetAttribute("alt");
        foreach (var attr in img.Attributes.Select(a => a.Name).ToList())
        {
            img.RemoveAttribute(attr);
        }

        img.SetAttribute("src", imageRef.Name);
        if (!string.IsNullOrWhiteSpace(alt))
        {
            img.SetAttribute("alt", alt);
        }

        images[imageRef.Name] = imageRef.ContentType;
    }

    private static bool IsUrlAttribute(string name) =>
        UrlAttributes.Any(a => name.Equals(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>True only for a plain absolute http(s) link — the only kind of navigable link we keep on a
    /// surviving element. Anything else (a pseudo-scheme, or a scheme-less intra-EPUB relative link) is
    /// dropped: the concatenated volume is served under one hash route, so a relative link would drive the
    /// SPA router somewhere bogus rather than jumping within the document.</summary>
    private static bool IsHttpUrl(string? url) =>
        url is not null
        && (url.TrimStart().StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.TrimStart().StartsWith("https://", StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the URL carries a scheme (contains "://" or a "scheme:" prefix before any
    /// slash/fragment). Intra-EPUB image refs are always relative, so anything with a scheme is either an
    /// external resource we won't proxy or a dangerous pseudo-scheme.</summary>
    private static bool IsExternal(string url)
    {
        var trimmed = url.TrimStart();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        // A colon that only appears after a path separator or fragment isn't a scheme (e.g. "a/b:c").
        var slash = trimmed.IndexOfAny(['/', '?', '#']);
        return slash < 0 || colon < slash;
    }
}
