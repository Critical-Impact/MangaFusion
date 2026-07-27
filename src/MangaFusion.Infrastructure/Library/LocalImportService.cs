using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Creates manually-curated series and imports local files as their chapters. Image files
/// (CBZ/CBR/folder/PDF/comic-EPUB) go through <see cref="ChapterFileImporter"/> — the same
/// <see cref="MangaFusion.Application.Writing.IChapterWriter"/>/ComicInfo.xml pipeline downloaded chapters
/// use. Prose files (EPUB/txt/md, only in a light-novel library) go through the parallel
/// <see cref="ProseChapterImporter"/> instead, producing an EPUB3 artifact for the text reader. Either
/// way the M4 reader serves the result like any downloaded chapter.</summary>
public sealed class LocalImportService(
    AppDbContext db, LibraryPaths paths, LocalPaths localPaths,
    ArtifactFileInspector artifactInspector, PdfPageExtractor pdfExtractor, CbrPageExtractor cbrExtractor,
    EpubPageExtractor epubExtractor, ChapterFileImporter chapterImporter, ProseChapterImporter proseImporter,
    AuthorResolver authorResolver, TagResolver tagResolver)
    : ILocalLibraryService
{
    public async Task<Guid> CreateSeriesAsync(LocalSeriesMetadata metadata, CancellationToken ct = default)
    {
        var series = new Series { Kind = metadata.Kind };
        series.SourceLinks.Add(new SeriesSourceLink
        {
            SourceId = LocalSourceConstants.SourceId,
            SourceSeriesId = Guid.NewGuid().ToString("N"),
            Kind = metadata.Kind,
            IsMetadataPrimary = true,
        });
        await ApplyMetadataAsync(series, metadata, ct);
        db.Series.Add(series);

        CacheCover(series, metadata.CoverFileName);
        await db.SaveChangesAsync(ct);
        return series.Id;
    }

    public async Task UpdateSeriesAsync(Guid seriesId, LocalSeriesMetadata metadata, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.SourceLinks).Include(s => s.Tags).Include(s => s.Authors).Include(s => s.Artists)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");
        EnsureLocal(series);

        await ApplyMetadataAsync(series, metadata, ct);
        CacheCover(series, metadata.CoverFileName);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The import-target picker: local series in <em>this</em> library only. Offering a manga
    /// series as a target while the user is importing a comic would put the file in the wrong library and
    /// the wrong directory tree.</summary>
    public async Task<IReadOnlyList<LocalSeriesSummary>> ListSeriesAsync(
        MediaKind kind, CancellationToken ct = default) =>
        await db.Series
            .Where(s => s.Kind == kind && s.SourceLinks.Any(l => l.SourceId == LocalSourceConstants.SourceId))
            .OrderBy(s => s.Title)
            .Select(s => new LocalSeriesSummary(s.Id, s.Title))
            .ToListAsync(ct);

    public Task<IReadOnlyList<InboxItem>> ListInboxAsync(MediaKind kind, CancellationToken ct = default)
    {
        var root = localPaths.InboxRoot(kind);
        var items = new List<InboxItem>();

        foreach (var file in Directory.EnumerateFiles(root))
        {
            var name = Path.GetFileName(file);
            var sourceKind = ClassifyForKind(file, kind);
            if (sourceKind is not null)
            {
                var pages = TryCountPages(file, sourceKind.Value);
                if (pages is not null)
                {
                    items.Add(new InboxItem(
                        name, KindLabel(sourceKind.Value), pages.Value, new FileInfo(file).Length,
                        IsProse(sourceKind.Value)));
                }
            }
            else if (ImageContentType.IsImage(name))
            {
                items.Add(new InboxItem(name, "image", 0, new FileInfo(file).Length));
            }
        }

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var pages = artifactInspector.CountFolderPages(dir);
            if (pages > 0)
            {
                items.Add(new InboxItem(Path.GetFileName(dir), "folder", pages, 0));
            }
        }

        return Task.FromResult<IReadOnlyList<InboxItem>>(
            items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Null on failure (e.g. a corrupt archive, or an EPUB that turns out to be reflowable
    /// text rather than an image-based comic) so one bad inbox file doesn't take down the whole
    /// listing — it's simply left out, same as any other file type the inbox doesn't recognize.</summary>
    private int? TryCountPages(string file, ChapterSourceKind kind)
    {
        try
        {
            return kind switch
            {
                ChapterSourceKind.Cbz => artifactInspector.CountCbzPages(file),
                ChapterSourceKind.Pdf => pdfExtractor.CountPages(file),
                ChapterSourceKind.Cbr => cbrExtractor.CountPages(file),
                ChapterSourceKind.Epub => epubExtractor.CountPages(file),
                // Page count is meaningless for prose (one file = one chapter); report 0 so the file still
                // lists (a non-null count) without opening it as pages. TODO: surface a word count instead.
                ChapterSourceKind.ProseEpub or ChapterSourceKind.ProsePdf
                    or ChapterSourceKind.ProseText or ChapterSourceKind.ProseMarkdown => 0,
                _ => throw new ArgumentOutOfRangeException(nameof(kind)),
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string KindLabel(ChapterSourceKind kind) => kind switch
    {
        ChapterSourceKind.Cbz => "cbz",
        ChapterSourceKind.Pdf => "pdf",
        ChapterSourceKind.Cbr => "cbr",
        ChapterSourceKind.Epub => "epub",
        ChapterSourceKind.Folder => "folder",
        ChapterSourceKind.ProseEpub => "epub",
        ChapterSourceKind.ProsePdf => "pdf",
        ChapterSourceKind.ProseText => "txt",
        ChapterSourceKind.ProseMarkdown => "md",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static ChapterSourceKind? ClassifyForKind(string filePath, MediaKind kind) =>
        ChapterSourceKindClassifier.ClassifyForKind(filePath, kind);

    private static bool IsProse(ChapterSourceKind kind) => ChapterSourceKindClassifier.IsProse(kind);

    public async Task<int> ImportAsync(Guid seriesId, LocalImportRequest request, CancellationToken ct = default)
    {
        var series = await db.Series
            .Include(s => s.SourceLinks)
            .Include(s => s.Chapters)
            .Include(s => s.Authors)
            .Include(s => s.Artists)
            .Include(s => s.Tags)
            .FirstOrDefaultAsync(s => s.Id == seriesId, ct)
            ?? throw new InvalidOperationException("Series not found.");
        EnsureLocal(series);

        // The series' own library decides which inbox the file is read from — the caller doesn't get to
        // pull a comic out of the manga inbox by naming it.
        var (sourcePath, sourceKind) = ResolveInboxEntry(series.Kind, request.FileName);
        var fileBaseName = LibraryPaths.Sanitize(Path.GetFileNameWithoutExtension(sourcePath));

        // Prose files write an EPUB3 via the parallel importer; everything else stays on the image path.
        return IsProse(sourceKind)
            ? await proseImporter.ImportAsync(
                series, sourcePath, sourceKind, fileBaseName, request.Language, request.Chapters, ct)
            : await chapterImporter.ImportAsync(
                series, sourcePath, sourceKind, fileBaseName, request.Language, request.Chapters, ct);
    }

    private (string Path, ChapterSourceKind Kind) ResolveInboxEntry(MediaKind kind, string fileName)
    {
        var root = localPaths.InboxRoot(kind);
        var full = Path.GetFullPath(Path.Combine(root, fileName));
        if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("File is outside the import inbox.");
        }

        if (File.Exists(full))
        {
            var sourceKind = ClassifyForKind(full, kind);
            if (sourceKind is not null)
            {
                return (full, sourceKind.Value);
            }
        }

        if (Directory.Exists(full))
        {
            return (full, ChapterSourceKind.Folder);
        }

        throw new InvalidOperationException(
            "Inbox entry not found (expected a .cbz/.cbr/.pdf/.epub/.txt/.md file or a folder).");
    }

    private void CacheCover(Series series, string? coverFileName)
    {
        if (string.IsNullOrWhiteSpace(coverFileName) || !ImageContentType.IsImage(coverFileName))
        {
            return;
        }

        var root = localPaths.InboxRoot(series.Kind);
        var src = Path.GetFullPath(Path.Combine(root, coverFileName));
        if (!src.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !File.Exists(src))
        {
            return;
        }

        var dir = paths.SeriesDirectory(series.Kind, series.Title);
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, "cover" + Path.GetExtension(coverFileName).ToLowerInvariant());
        File.Copy(src, dest, overwrite: true);
        series.CoverPath = paths.RelativeTo(series.Kind, dest);
        series.CoverUpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void EnsureLocal(Series series)
    {
        var isLocal = series.SourceLinks.Any(l => l.SourceId == LocalSourceConstants.SourceId);
        if (!isLocal)
        {
            throw new InvalidOperationException("This is not a local series.");
        }
    }

    private async Task ApplyMetadataAsync(Series series, LocalSeriesMetadata m, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(m.Title))
        {
            throw new InvalidOperationException("A title is required.");
        }

        series.Title = m.Title.Trim();
        series.AltTitles = m.AltTitles?.ToList() ?? [];
        series.Authors = await authorResolver.ResolveOrCreateByNameAsync(m.Authors ?? [], ct);
        series.Artists = [];
        series.Tags = await tagResolver.ResolveOrCreateByNameAsync(series.Kind, m.Tags ?? [], ct);
        series.Description = m.Description;
        series.ContentRating = ParseEnum(m.ContentRating, ContentRating.Unknown);
        series.Status = ParseEnum(m.Status, PublicationStatus.Unknown);
        series.Year = m.Year;
        series.OriginalLanguage = m.OriginalLanguage;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
