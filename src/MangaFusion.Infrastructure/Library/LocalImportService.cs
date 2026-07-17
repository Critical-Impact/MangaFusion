using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Creates manually-curated series and imports local CBZ/folder/PDF files as their chapters.
/// Each source file is rasterized/re-encoded through <see cref="ChapterFileImporter"/> — the same
/// <see cref="MangaFusion.Application.Writing.IChapterWriter"/>/ComicInfo.xml pipeline downloaded
/// chapters use — so manually-imported output lands in the same on-disk format as the rest of the
/// library, and the M4 reader serves it like any downloaded chapter.</summary>
public sealed class LocalImportService(
    AppDbContext db, LibraryPaths paths, LocalPaths localPaths,
    ArtifactFileInspector artifactInspector, PdfPageExtractor pdfExtractor, ChapterFileImporter chapterImporter,
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
            if (name.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new InboxItem(name, "cbz", artifactInspector.CountCbzPages(file), new FileInfo(file).Length));
            }
            else if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new InboxItem(name, "pdf", pdfExtractor.CountPages(file), new FileInfo(file).Length));
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
        return await chapterImporter.ImportAsync(
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

        if (File.Exists(full) && full.EndsWith(".cbz", StringComparison.OrdinalIgnoreCase))
        {
            return (full, ChapterSourceKind.Cbz);
        }

        if (File.Exists(full) && full.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return (full, ChapterSourceKind.Pdf);
        }

        if (Directory.Exists(full))
        {
            return (full, ChapterSourceKind.Folder);
        }

        throw new InvalidOperationException("Inbox entry not found (expected a .cbz/.pdf file or a folder).");
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
