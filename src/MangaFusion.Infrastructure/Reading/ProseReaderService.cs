using AngleSharp.Html.Parser;
using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Reads prose chapters for the in-app text reader and records per-user scroll progress. Runs
/// alongside <see cref="ReaderService"/> (which it deliberately does not touch): chapter navigation, the
/// reading rail and Continue-reading stay on <c>IReaderService</c>, kind-agnostic already. Prose page
/// counts are reinterpreted at chapter granularity (1 per chapter), so a chapter's spine position inside
/// a multi-chapter EPUB is just its order among the artifact's chapter links.</summary>
public sealed class ProseReaderService(
    AppDbContext db,
    IProseArtifactReader proseReader,
    LibraryPaths paths) : IProseReaderService
{
    public async Task<ProseManifest?> GetProseManifestAsync(
        Guid userId, Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Series)
            .Include(c => c.ActiveArtifact!)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null || chapter.ActiveArtifact.Format != StorageFormat.Prose)
        {
            return null;
        }

        var progress = await db.ReadingProgress
            .Where(p => p.UserId == userId && p.ChapterId == chapterId)
            .Select(p => new { p.ScrollFraction, p.Completed })
            .FirstOrDefaultAsync(ct);

        return new ProseManifest(
            chapter.Id,
            chapter.ActiveArtifact.Id,
            chapter.SeriesId,
            chapter.Series.Title,
            chapter.Number,
            chapter.Volume,
            chapter.Language,
            Math.Clamp(progress?.ScrollFraction ?? 0f, 0f, 1f),
            progress?.Completed ?? false);
    }

    public async Task<ProseContent?> GetProseContentAsync(Guid chapterId, CancellationToken ct = default)
    {
        if (await ResolveArtifactAsync(chapterId, ct) is not { } resolved)
        {
            return null;
        }

        var (artifact, kind) = resolved;
        var content = await proseReader.ReadBookAsync(paths.Absolute(kind, artifact.Path), ct);
        if (content is null)
        {
            return null;
        }

        var html = content.ImageContentTypes.Count == 0
            ? content.Html
            : RewriteImageUrls(content.Html, chapterId);
        return new ProseContent(html, content.WordCount);
    }

    public async Task<OpenPageResult?> OpenProseImageAsync(
        Guid chapterId, string imageName, string? ifNoneMatch = null, CancellationToken ct = default)
    {
        if (await ResolveArtifactAsync(chapterId, ct) is not { } resolved)
        {
            return null;
        }

        var (artifact, kind) = resolved;
        var etag = $"\"{artifact.Hash}:{imageName}\"";
        if (ifNoneMatch == etag)
        {
            return new OpenPageResult(null, null, etag);
        }

        var image = await proseReader.OpenImageAsync(paths.Absolute(kind, artifact.Path), imageName, ct);
        if (image is null)
        {
            return null;
        }

        return new OpenPageResult(image.Stream, image.ContentType, etag);
    }

    public async Task SaveProseProgressAsync(
        Guid userId, Guid chapterId, float scrollFraction, bool completed, CancellationToken ct = default)
    {
        // Chapter must exist, but no artifact/window math is needed: prose progress is a single scroll
        // fraction, not a page index into an archive.
        var exists = await db.Chapters.AnyAsync(c => c.Id == chapterId, ct);
        if (!exists)
        {
            throw new InvalidOperationException("Chapter not found.");
        }

        var clamped = Math.Clamp(scrollFraction, 0f, 1f);
        var isComplete = completed || clamped >= 0.96f;

        var progress = await db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId, ChapterId = chapterId };
            db.ReadingProgress.Add(progress);
        }

        // PageIndex stays 0 (a prose chapter is a 1-"page" window); ScrollFraction carries the fine
        // resume position, Completed drives Continue-reading/neighbours exactly as for image chapters.
        progress.PageIndex = 0;
        progress.ScrollFraction = clamped;
        progress.Completed = isComplete;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ProsePdfFile?> ResolvePdfAsync(Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Series)
            .Include(c => c.ActiveArtifact!)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null || chapter.ActiveArtifact.Format != StorageFormat.Pdf)
        {
            return null;
        }

        var artifact = chapter.ActiveArtifact;
        return new ProsePdfFile(paths.Absolute(chapter.Series.Kind, artifact.Path), $"\"{artifact.Hash}\"");
    }

    public async Task<ProsePdfManifest?> GetPdfManifestAsync(
        Guid userId, Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Series)
            .Include(c => c.ActiveArtifact!)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null || chapter.ActiveArtifact.Format != StorageFormat.Pdf)
        {
            return null;
        }

        var progress = await db.ReadingProgress
            .Where(p => p.UserId == userId && p.ChapterId == chapterId)
            .Select(p => new { p.PageIndex, p.Completed })
            .FirstOrDefaultAsync(ct);

        return new ProsePdfManifest(
            chapter.Id, chapter.SeriesId, chapter.Series.Title, chapter.Number, chapter.Volume,
            chapter.Language, Math.Max(0, progress?.PageIndex ?? 0), progress?.Completed ?? false);
    }

    public async Task SavePdfProgressAsync(
        Guid userId, Guid chapterId, int page, bool completed, CancellationToken ct = default)
    {
        var exists = await db.Chapters.AnyAsync(c => c.Id == chapterId, ct);
        if (!exists)
        {
            throw new InvalidOperationException("Chapter not found.");
        }

        var progress = await db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId, ChapterId = chapterId };
            db.ReadingProgress.Add(progress);
        }

        // PDF resume is a page index (the artifact's own page window is 1 — a whole-volume PDF — so this
        // deliberately isn't clamped to it). ScrollFraction is EPUB-only and left null.
        progress.PageIndex = Math.Max(0, page);
        progress.ScrollFraction = null;
        progress.Completed = completed;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Loads a prose chapter's active artifact plus the kind (to resolve its on-disk path). Null
    /// when the chapter has no prose artifact. One prose artifact = one whole-volume EPUB = one chapter,
    /// so there's no spine index to resolve — the reader renders the whole book.</summary>
    private async Task<(Artifact Artifact, MediaKind Kind)?> ResolveArtifactAsync(
        Guid chapterId, CancellationToken ct)
    {
        var chapter = await db.Chapters
            .Include(c => c.Series)
            .Include(c => c.ActiveArtifact!)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null || chapter.ActiveArtifact.Format != StorageFormat.Prose)
        {
            return null;
        }

        return (chapter.ActiveArtifact, chapter.Series.Kind);
    }

    /// <summary>Rewrites the sanitizer's bare <c>&lt;img src="{name}"&gt;</c> to the absolute image
    /// endpoint URL for this chapter. Only called when a chapter actually has inline images.</summary>
    private static string RewriteImageUrls(string html, Guid chapterId)
    {
        var parser = new HtmlParser();
        var doc = parser.ParseDocument(html);
        var body = doc.Body;
        if (body is null)
        {
            return html;
        }

        foreach (var img in body.QuerySelectorAll("img"))
        {
            var name = img.GetAttribute("src");
            if (!string.IsNullOrEmpty(name))
            {
                img.SetAttribute("src", $"/api/library/chapters/{chapterId}/prose/images/{name}");
            }
        }

        return body.InnerHtml;
    }
}
