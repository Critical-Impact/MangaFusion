using System.Net;
using System.Text;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Turns a plain <c>.txt</c> file into chapter HTML: blank-line-separated blocks become
/// <c>&lt;p&gt;</c> paragraphs, single line breaks within a block become <c>&lt;br/&gt;</c>, and all text
/// is HTML-entity-escaped (the read path sanitizes too, but escaping at the source keeps the stored EPUB
/// honest). One file = one chapter.</summary>
internal static class ProseTextExtractor
{
    public static async Task<string> ExtractAsync(string path, CancellationToken ct) =>
        ToHtml(await File.ReadAllTextAsync(path, ct));

    public static string ToHtml(string text)
    {
        var normalized = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var blocks = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);

        var sb = new StringBuilder();
        foreach (var block in blocks)
        {
            var trimmed = block.Trim('\n');
            if (trimmed.Length == 0)
            {
                continue;
            }

            var lines = trimmed.Split('\n').Select(l => WebUtility.HtmlEncode(l.Trim()));
            sb.Append("<p>").Append(string.Join("<br/>", lines)).Append("</p>\n");
        }

        if (sb.Length == 0)
        {
            throw new InvalidOperationException("The text file is empty — nothing to import.");
        }

        return sb.ToString();
    }
}
