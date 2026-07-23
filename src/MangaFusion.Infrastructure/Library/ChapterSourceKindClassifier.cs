namespace MangaFusion.Infrastructure.Library;

/// <summary>Classifies an import source file by extension — the one place both entry points (the
/// manual local-import inbox in <see cref="LocalImportService"/> and the MangaUpdates-assisted import
/// wizard's <see cref="ImportScanner"/>) sniff a <see cref="ChapterSourceKind"/> from a file name, so
/// the recognized-extension list only has to change in one place.</summary>
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
}
