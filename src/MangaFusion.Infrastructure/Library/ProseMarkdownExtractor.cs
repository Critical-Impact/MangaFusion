using Markdig;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Turns a <c>.md</c> file into chapter HTML via Markdig. Any images or links Markdig emits are
/// left as-is here — the read-path sanitizer (<c>ProseHtmlSanitizer</c>) drops external/unsafe URLs, and
/// a local Markdown file has no inline images bundled to resolve against. One file = one chapter.</summary>
internal static class ProseMarkdownExtractor
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static async Task<string> ExtractAsync(string path, CancellationToken ct)
    {
        var markdown = await File.ReadAllTextAsync(path, ct);
        var html = Markdown.ToHtml(markdown, Pipeline);
        if (string.IsNullOrWhiteSpace(html))
        {
            throw new InvalidOperationException("The markdown file is empty — nothing to import.");
        }

        return html;
    }
}
