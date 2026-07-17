using AngleSharp.Dom;

namespace MangaFusion.Sources.Web.Parsing;

/// <summary>Small Jsoup-flavoured conveniences over AngleSharp, to keep ported parse code terse and
/// null-safe (Jsoup's <c>select</c>/<c>text</c>/<c>attr</c> never NPE on a missing node).</summary>
public static class HtmlExtensions
{
    /// <summary>Trimmed text content, or "" if the element is null.</summary>
    public static string Text(this IElement? element) => element?.TextContent.Trim() ?? "";

    /// <summary>Text of the element's own direct text nodes only (Jsoup's <c>ownText()</c>), excluding
    /// descendant elements' text. "" if the element is null.</summary>
    public static string OwnText(this IElement? element) =>
        element is null
            ? ""
            : string.Concat(element.ChildNodes.OfType<IText>().Select(t => t.Text)).Trim();

    /// <summary>Descendants matching the selector whose text contains <paramref name="text"/> — a stand-in
    /// for Jsoup's <c>:contains(...)</c> pseudo-class, which AngleSharp doesn't support.</summary>
    public static IEnumerable<IElement> SelectContaining(this IParentNode? node, string selector, string text) =>
        node.Select(selector).Where(e => e.TextContent.Contains(text, StringComparison.OrdinalIgnoreCase));

    /// <summary>Attribute value, or "" if the element or attribute is missing.</summary>
    public static string Attr(this IElement? element, string name) => element?.GetAttribute(name) ?? "";

    /// <summary>First descendant matching the CSS selector, or null.</summary>
    public static IElement? SelectFirst(this IParentNode? node, string selector) =>
        node?.QuerySelector(selector);

    /// <summary>All descendants matching the CSS selector (empty if none / null node).</summary>
    public static IEnumerable<IElement> Select(this IParentNode? node, string selector) =>
        node?.QuerySelectorAll(selector) ?? Enumerable.Empty<IElement>();
}
