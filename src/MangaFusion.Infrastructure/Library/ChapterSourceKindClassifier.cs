using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Classifies an import source file into a <see cref="ChapterSourceKind"/> — the one place both
/// entry points (the manual local-import inbox in <see cref="LocalImportService"/> and the
/// MangaUpdates-assisted import wizard's <see cref="ImportScanner"/>) decide what a file is, so the rules
/// live in one spot. <see cref="FromFileName"/> is the pure extension sniff; <see cref="ClassifyForKind"/>
/// layers on content detection for light novels, where an EPUB or PDF may be either real text or a scan.</summary>
public static class ChapterSourceKindClassifier
{
    public static ChapterSourceKind? FromFileName(string fileName)
    {
        if (fileName.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            return ChapterSourceKind.Cbz;
        }

        if (fileName.EndsWith(".cbr", StringComparison.OrdinalIgnoreCase))
        {
            return ChapterSourceKind.Cbr;
        }

        if (fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return ChapterSourceKind.Pdf;
        }

        if (fileName.EndsWith(".epub", StringComparison.OrdinalIgnoreCase))
        {
            return ChapterSourceKind.Epub;
        }

        return null;
    }

    /// <summary>Classifies a file for a target library. Non-light-novel libraries use the pure extension
    /// sniff. In a light-novel library: an EPUB is probed (real text/mixed ⇒ prose EPUB3, stored as-is; a
    /// pure-image scan ⇒ the ordinary image page pipeline); a PDF is always kept verbatim (<c>ProsePdf</c>,
    /// rendered fixed-layout by PDF.js — no lossy text/rasterize conversion); <c>.txt</c>/<c>.md</c> are
    /// prose; CBZ/CBR/folders are images. The EPUB probe is wrapped so a corrupt file falls back to the
    /// image path rather than being dropped.</summary>
    public static ChapterSourceKind? ClassifyForKind(string filePath, MediaKind kind)
    {
        var baseKind = FromFileName(filePath);
        if (kind != MediaKind.LightNovel)
        {
            return baseKind;
        }

        switch (baseKind)
        {
            case ChapterSourceKind.Epub:
                return Probe(EpubContentClassifier.IsProse, filePath) ? ChapterSourceKind.ProseEpub : ChapterSourceKind.Epub;
            case ChapterSourceKind.Pdf:
                return ChapterSourceKind.ProsePdf;
            case null when filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase):
                return ChapterSourceKind.ProseText;
            case null when filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase):
                return ChapterSourceKind.ProseMarkdown;
            default:
                return baseKind; // CBZ/CBR/folder ⇒ images
        }
    }

    /// <summary>Whether a kind is prose (routed to <see cref="ProseChapterImporter"/> / the text reader)
    /// rather than page images.</summary>
    public static bool IsProse(ChapterSourceKind kind) =>
        kind is ChapterSourceKind.ProseEpub or ChapterSourceKind.ProsePdf
            or ChapterSourceKind.ProseText or ChapterSourceKind.ProseMarkdown;

    private static bool Probe(Func<string, bool> classifier, string path)
    {
        try
        {
            return classifier(path);
        }
        catch
        {
            return false; // undetectable ⇒ treat as image; the image path will validate or reject it
        }
    }
}
