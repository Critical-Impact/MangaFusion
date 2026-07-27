using System.Security.Cryptography;
using MangaFusion.Application.Library;
using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Imports one local light-novel file as a single whole-volume chapter. An <b>EPUB</b> is stored
/// as-is (already an EPUB3 text+image container — <see cref="StorageFormat.Prose"/>, read by the text
/// reader). A <b>PDF</b> is also stored as-is (<see cref="StorageFormat.Pdf"/>, rendered fixed-layout by
/// the client's PDF.js reader) — keeping it verbatim preserves the cover/illustrations/TOC/layout that a
/// PDF→EPUB conversion strips. A <b>txt/md</b> source has no rich container, so its text is wrapped into a
/// fresh EPUB3 via <see cref="IProseChapterWriter"/> (<see cref="StorageFormat.Prose"/>). One artifact =
/// one chapter (v1: one file = one volume). A parallel importer to <see cref="ChapterFileImporter"/> (the
/// image path), sharing only the pure dedup check in <see cref="ChapterKeys"/>.</summary>
public sealed class ProseChapterImporter(
    AppDbContext db, LibraryPaths paths, IProseChapterWriter proseWriter)
{
    private sealed record StoredArtifact(string Path, long SizeBytes, string Sha256);

    /// <summary>Imports <paramref name="sourceAbsolutePath"/> as one chapter of <paramref name="series"/>.
    /// Only the first spec is used — prose has no page-count-based splitting. Returns 1 on success. The
    /// caller must have loaded <paramref name="series"/>'s <c>Chapters</c>/<c>Authors</c>/<c>Tags</c>.</summary>
    public async Task<int> ImportAsync(
        Series series, string sourceAbsolutePath, ChapterSourceKind sourceKind, string fileBaseName,
        string language, IReadOnlyList<LocalChapterSpec> chapters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidOperationException("A language is required.");
        }

        if (chapters.Count == 0)
        {
            throw new InvalidOperationException("At least one chapter is required.");
        }

        var spec = chapters[0];
        var (_, key) = ChapterKeys.KeyAndEnsureUnique(series, language, [spec])[0];

        var targetDir = paths.SeriesDirectory(series.Kind, series.Title);
        var (stored, format) = sourceKind switch
        {
            ChapterSourceKind.ProseEpub =>
                (await StoreAsIsAsync(sourceAbsolutePath, targetDir, fileBaseName, ".epub", ct), StorageFormat.Prose),
            ChapterSourceKind.ProsePdf =>
                (await StoreAsIsAsync(sourceAbsolutePath, targetDir, fileBaseName, ".pdf", ct), StorageFormat.Pdf),
            _ => (await WriteProseEpubAsync(series, sourceAbsolutePath, sourceKind, spec, targetDir, fileBaseName, language, ct),
                StorageFormat.Prose),
        };

        // PageCount is reinterpreted at chapter granularity for prose: one whole-volume chapter = 1.
        var artifact = new Artifact
        {
            SeriesId = series.Id,
            Format = format,
            Origin = ArtifactOrigin.Local,
            Path = paths.RelativeTo(series.Kind, stored.Path),
            PageCount = 1,
            Hash = stored.Sha256,
            Status = ArtifactStatus.Complete,
            SizeBytes = stored.SizeBytes,
        };
        db.Artifacts.Add(artifact);

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
            SourceChapterId = $"{artifact.Id:N}:0",
            ScanlationGroups = [],
            GroupKey = null,
            PublishedAt = DateTimeOffset.UtcNow,
            PageCount = 1,
            IsExternal = false,
        };
        chapter.Releases.Add(release);
        db.ChapterReleases.Add(release);

        artifact.ChapterLinks.Add(new ArtifactChapter { ChapterId = chapter.Id, Order = 0, PageCount = 1 });

        // Insert chapter + release first, then point the chapter at its active release (setting it up front
        // makes EF see a Chapter<->ChapterRelease insert cycle). Same ordering as the sibling importer.
        await db.SaveChangesAsync(ct);
        chapter.ActiveReleaseId = release.Id;
        await db.SaveChangesAsync(ct);

        return 1;
    }

    /// <summary>Copies a source file (EPUB/PDF) verbatim into the library as the artifact, temp-file-then-
    /// move onto a <see cref="LibraryPaths.UniquePath"/> so a partial/colliding write is never left behind
    /// (mirrors the writers' crash-safety).</summary>
    private static async Task<StoredArtifact> StoreAsIsAsync(
        string sourcePath, string targetDir, string fileBaseName, string extension, CancellationToken ct)
    {
        Directory.CreateDirectory(targetDir);
        var path = LibraryPaths.UniquePath(Path.Combine(targetDir, fileBaseName + extension));
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";

        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var dest = File.Create(tempPath))
            {
                await source.CopyToAsync(dest, ct);
            }

            var (hash, size) = await HashAsync(tempPath, ct);
            File.Move(tempPath, path, overwrite: false);
            return new StoredArtifact(path, size, hash);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>Wraps a non-EPUB text source (txt/md/text-PDF) into a fresh EPUB3 via the prose writer.
    /// These carry no bundled images to preserve, so a single text segment is all that's needed.</summary>
    private async Task<StoredArtifact> WriteProseEpubAsync(
        Series series, string sourceAbsolutePath, ChapterSourceKind sourceKind, LocalChapterSpec spec,
        string targetDir, string fileBaseName, string language, CancellationToken ct)
    {
        var html = sourceKind switch
        {
            ChapterSourceKind.ProseText => await ProseTextExtractor.ExtractAsync(sourceAbsolutePath, ct),
            ChapterSourceKind.ProseMarkdown => await ProseMarkdownExtractor.ExtractAsync(sourceAbsolutePath, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Not a writable prose kind."),
        };

        var segment = new ProseChapterSegment(
            spec.Number, spec.Volume, spec.Title, language, html, new Dictionary<string, string>());

        var request = new ProseWriteRequest(
            series.Title,
            series.Authors.Select(a => a.Name).ToList(),
            series.Tags.Where(t => t.Group == "genre").Select(t => t.Name).ToList(),
            targetDir,
            fileBaseName,
            [segment],
            Artists: series.Artists.Select(a => a.Name).ToList(),
            OtherTags: series.Tags.Where(t => t.Group != "genre").Select(t => t.Name).ToList(),
            Description: series.Description,
            ContentRating: series.ContentRating);

        var result = await proseWriter.WriteAsync(request, ct);
        return new StoredArtifact(result.Path, result.SizeBytes, result.Sha256);
    }

    private static async Task<(string Hash, long Size)> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return (Convert.ToHexStringLower(hash), stream.Length);
    }
}
