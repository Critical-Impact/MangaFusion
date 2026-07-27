using System.IO.Compression;
using MangaFusion.Application.Library;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;

namespace MangaFusion.Infrastructure.Library;

/// <summary>How to read pages out of an import source file — distinct from <see cref="StorageFormat"/>,
/// which describes how the resulting library artifact is stored (always Cbz or Folder; a PDF is never
/// stored as-is, it's rasterized into whichever format the library writes by default).</summary>
public enum ChapterSourceKind
{
    Cbz,
    Folder,
    Pdf,
    Cbr,
    Epub,

    // Prose (light-novel) source kinds. These never reach the image-page ChapterFileImporter — they're
    // dispatched to ProseChapterImporter — but they are members of this shared enum so the inbox listing
    // and classifier can name them. A whole file imports as one chapter (no page-count splitting).
    // ProseEpub/ProsePdf are the text-bearing variants of Epub/Pdf, chosen by content detection (a
    // light-novel library can hold both a scanned image comic and a real text novel).
    ProseEpub,
    ProsePdf,
    ProseText,
    ProseMarkdown,
}

/// <summary>Turns one source file (CBZ, image folder, or PDF) plus a set of chapter specs into a
/// library <see cref="Artifact"/>/<see cref="Chapter"/>/<see cref="ChapterRelease"/> via the same
/// <see cref="IChapterWriter"/>/ComicInfo.xml pipeline downloaded chapters use — so manually-imported
/// output lands in the same on-disk format as the rest of the library. Shared by
/// <see cref="LocalImportService"/> and the MangaUpdates-assisted import wizard.</summary>
public sealed class ChapterFileImporter(
    AppDbContext db, LibraryPaths paths, ChapterWriterSelector writers,
    ArtifactFileInspector artifactInspector, PdfPageExtractor pdfExtractor,
    CbrPageExtractor cbrExtractor, EpubPageExtractor epubExtractor)
{
    /// <summary>Imports one source file as one or more chapters of <paramref name="series"/>, carving
    /// it per <paramref name="chapters"/>. Returns the number of chapters added. The caller is
    /// responsible for ensuring <paramref name="series"/>'s <c>Chapters</c> navigation is loaded.
    /// <paramref name="pageProgress"/>, if given, only ever reports for a PDF source (the slow case —
    /// CBZ/folder extraction is comparatively instant, not worth reporting on).</summary>
    public async Task<int> ImportAsync(
        Series series, string sourceAbsolutePath, ChapterSourceKind sourceKind, string fileBaseName,
        string language, IReadOnlyList<LocalChapterSpec> chapters, CancellationToken ct,
        IProgress<int>? pageProgress = null)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidOperationException("A language is required.");
        }

        var total = CountPages(sourceAbsolutePath, sourceKind);
        if (total == 0)
        {
            throw new InvalidOperationException("The file contains no page images.");
        }

        var specs = NormalizeSpecs(chapters, total);

        // Reject duplicate chapter numbers (both within the request and against existing chapters).
        var keyed = ChapterKeys.KeyAndEnsureUnique(series, language, specs);

        // Not the OS temp dir — see LibraryPaths.TempRoot for why (small tmpfs in containers can run
        // out of space partway through rasterizing a large PDF, exactly as happened in practice).
        var tempDir = paths.NewTempDirectory("mf-import");
        try
        {
            var pagePaths = await ExtractPagesAsync(sourceAbsolutePath, sourceKind, tempDir, pageProgress, ct);

            var writer = writers.Get();
            var segments = new List<ChapterSegment>();
            var offset = 0;
            foreach (var spec in specs)
            {
                var pages = pagePaths.Skip(offset).Take(spec.PageCount)
                    .Select((p, i) => new PageFile(i, Path.GetFileName(p), p))
                    .ToList();
                segments.Add(new ChapterSegment(spec.Number, spec.Volume, spec.Title, language, null, pages));
                offset += spec.PageCount;
            }

            var request = new WriteRequest(
                series.Title,
                series.Authors.Select(a => a.Name).ToList(),
                series.Tags.Where(t => t.Group == "genre").Select(t => t.Name).ToList(),
                writer.Format,
                paths.SeriesDirectory(series.Kind, series.Title),
                fileBaseName,
                segments,
                Artists: series.Artists.Select(a => a.Name).ToList(),
                OtherTags: series.Tags.Where(t => t.Group != "genre").Select(t => t.Name).ToList(),
                Description: series.Description,
                ContentRating: series.ContentRating,
                OriginalLanguage: series.OriginalLanguage,
                AltTitles: series.AltTitles,
                Kind: series.Kind);

            var result = await writer.WriteAsync(request, null, ct);

            var artifact = new Artifact
            {
                SeriesId = series.Id,
                Format = writer.Format,
                Origin = ArtifactOrigin.Local,
                Path = paths.RelativeTo(series.Kind, result.Path),
                PageCount = result.PageCount,
                Hash = result.Sha256,
                Status = ArtifactStatus.Complete,
                SizeBytes = result.SizeBytes,
            };
            db.Artifacts.Add(artifact);

            var order = 0;
            var pairs = new List<(Chapter Chapter, ChapterRelease Release)>();
            foreach (var (spec, key) in keyed)
            {
                var (sort, _) = ChapterNumber.Normalize(spec.Number, spec.Volume);
                var chapter = new Chapter
                {
                    SeriesId = series.Id,
                    Language = language,
                    Number = spec.Number,
                    NumberSort = sort,
                    NumberKey = key,
                    Volume = spec.Volume,
                    VolumeSort = ChapterNumber.VolumeSort(spec.Volume),
                    Title = spec.Title,
                    ActiveArtifactId = artifact.Id,
                };
                db.Chapters.Add(chapter);

                var release = new ChapterRelease
                {
                    SourceId = LocalSourceConstants.SourceId,
                    SourceChapterId = $"{artifact.Id:N}:{order}",
                    ScanlationGroups = [],
                    GroupKey = null,
                    PublishedAt = DateTimeOffset.UtcNow,
                    PageCount = spec.PageCount,
                    IsExternal = false,
                };
                chapter.Releases.Add(release);
                db.ChapterReleases.Add(release);

                artifact.ChapterLinks.Add(new ArtifactChapter
                {
                    ChapterId = chapter.Id,
                    Order = order,
                    PageCount = spec.PageCount,
                });

                pairs.Add((chapter, release));
                order++;
            }

            // Insert chapters + releases first, then point each chapter at its active release. Setting
            // ActiveReleaseId up front would make EF see a Chapter<->ChapterRelease insert cycle (both new).
            await db.SaveChangesAsync(ct);
            foreach (var (chapter, release) in pairs)
            {
                chapter.ActiveReleaseId = release.Id;
            }

            await db.SaveChangesAsync(ct);
            return specs.Count;
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    public int CountPages(string sourceAbsolutePath, ChapterSourceKind sourceKind) => sourceKind switch
    {
        ChapterSourceKind.Cbz => artifactInspector.CountCbzPages(sourceAbsolutePath),
        ChapterSourceKind.Folder => artifactInspector.CountFolderPages(sourceAbsolutePath),
        ChapterSourceKind.Pdf => pdfExtractor.CountPages(sourceAbsolutePath),
        ChapterSourceKind.Cbr => cbrExtractor.CountPages(sourceAbsolutePath),
        ChapterSourceKind.Epub => epubExtractor.CountPages(sourceAbsolutePath),
        _ => throw new ArgumentOutOfRangeException(nameof(sourceKind)),
    };

    private async Task<List<string>> ExtractPagesAsync(
        string sourceAbsolutePath, ChapterSourceKind sourceKind, string tempDir, IProgress<int>? pageProgress,
        CancellationToken ct)
    {
        switch (sourceKind)
        {
            case ChapterSourceKind.Folder:
                return Directory.EnumerateFiles(sourceAbsolutePath)
                    .Where(f => ImageContentType.IsImage(Path.GetFileName(f)))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

            case ChapterSourceKind.Pdf:
                return await pdfExtractor.ExtractPagesAsync(sourceAbsolutePath, tempDir, pageProgress, ct);

            case ChapterSourceKind.Cbz:
                return await ExtractCbzPagesAsync(sourceAbsolutePath, tempDir, ct);

            case ChapterSourceKind.Cbr:
                return await cbrExtractor.ExtractPagesAsync(sourceAbsolutePath, tempDir, ct);

            case ChapterSourceKind.Epub:
                return await epubExtractor.ExtractPagesAsync(sourceAbsolutePath, tempDir, ct);

            default:
                throw new ArgumentOutOfRangeException(nameof(sourceKind));
        }
    }

    private static async Task<List<string>> ExtractCbzPagesAsync(string cbzPath, string tempDir, CancellationToken ct)
    {
        Directory.CreateDirectory(tempDir);
        var results = new List<string>();

        using var zip = ZipFile.OpenRead(cbzPath);
        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) && ImageContentType.IsImage(e.Name))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);

        var index = 0;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(tempDir, $"{(index + 1):D5}{Path.GetExtension(entry.Name)}");
            await using (var entryStream = entry.Open())
            await using (var fileStream = File.Create(dest))
            {
                await entryStream.CopyToAsync(fileStream, ct);
            }

            results.Add(dest);
            index++;
        }

        return results;
    }

    /// <summary>Fills in page counts for a single "whole file" spec and validates that the per-chapter
    /// counts add up to the file's total.</summary>
    private static List<LocalChapterSpec> NormalizeSpecs(IReadOnlyList<LocalChapterSpec> specs, int total)
    {
        if (specs.Count == 0)
        {
            throw new InvalidOperationException("At least one chapter is required.");
        }

        if (specs.Count == 1 && specs[0].PageCount <= 0)
        {
            return [specs[0] with { PageCount = total }];
        }

        if (specs.Any(s => s.PageCount <= 0))
        {
            throw new InvalidOperationException("Each chapter must have a positive page count.");
        }

        if (specs.Sum(s => s.PageCount) != total)
        {
            throw new InvalidOperationException(
                $"Chapter page counts must sum to the file's {total} pages.");
        }

        return specs.ToList();
    }
}
