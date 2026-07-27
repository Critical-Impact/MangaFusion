using MangaFusion.Application.Reading;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Reading;

/// <summary>Reads downloaded chapters for the in-app reader and records per-user progress. Page counts
/// come from <see cref="Artifact.PageCount"/> (authoritative — set when written), so the manifest and
/// progress paths touch only the DB; the archive is opened only to stream actual page bytes.</summary>
public sealed class ReaderService(
    AppDbContext db,
    ArtifactReaderRegistry readers,
    LibraryPaths paths) : IReaderService
{
    public async Task<string?> GetReaderKindAsync(Guid chapterId, CancellationToken ct = default)
    {
        var format = await db.Chapters
            .Where(c => c.Id == chapterId && c.ActiveArtifactId != null)
            .Select(c => (StorageFormat?)c.ActiveArtifact!.Format)
            .FirstOrDefaultAsync(ct);

        return format switch
        {
            null => null,
            StorageFormat.Prose => "prose",
            StorageFormat.Pdf => "pdf",
            _ => "image",
        };
    }

    public async Task<ChapterManifest?> GetManifestAsync(Guid userId, Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.Series)
            .Include(c => c.ActiveArtifact!).ThenInclude(a => a.ChapterLinks)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null)
        {
            return null;
        }

        var (_, length) = await ComputeWindowAsync(chapter.ActiveArtifact, chapter.Id, ct);
        if (length <= 0)
        {
            return null;
        }

        var saved = await db.ReadingProgress
            .Where(p => p.UserId == userId && p.ChapterId == chapterId)
            .Select(p => (int?)p.PageIndex)
            .FirstOrDefaultAsync(ct) ?? 0;

        return new ChapterManifest(
            chapter.Id,
            chapter.ActiveArtifact.Id,
            length,
            Math.Clamp(saved, 0, length - 1),
            DeriveDirection(chapter.Series.Kind, chapter.Series.OriginalLanguage),
            chapter.SeriesId,
            chapter.Series.Title,
            chapter.Number,
            chapter.Volume,
            chapter.Language);
    }

    public async Task<OpenPageResult?> OpenPageAsync(
        Guid chapterId, int pageIndex, string? ifNoneMatch = null, CancellationToken ct = default)
    {
        if (pageIndex < 0)
        {
            return null;
        }

        var chapter = await db.Chapters
            .Include(c => c.ActiveArtifact!).ThenInclude(a => a.ChapterLinks)
            // Series is joined in for its Kind alone: an artifact's stored path is relative to its own
            // library's root, so it can't be turned into a file without knowing which library it's in.
            .Include(c => c.Series)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct);

        if (chapter?.ActiveArtifact is null)
        {
            return null;
        }

        var artifact = chapter.ActiveArtifact;
        var (offset, length) = await ComputeWindowAsync(artifact, chapter.Id, ct);
        if (pageIndex >= length)
        {
            return null;
        }

        var globalIndex = offset + pageIndex;
        var etag = $"\"{artifact.Hash}:{globalIndex}\"";

        // Content is immutable per artifact hash, so a matching If-None-Match means the client already
        // has these exact bytes — skip opening/decompressing the archive entirely.
        if (ifNoneMatch == etag)
        {
            return new OpenPageResult(null, null, etag);
        }

        var content = await readers.Get(artifact.Format)
            .OpenPageAsync(paths.Absolute(chapter.Series.Kind, artifact.Path), globalIndex, ct);
        if (content is null)
        {
            return null;
        }

        return new OpenPageResult(content.Stream, content.ContentType, etag);
    }

    public async Task SaveProgressAsync(
        Guid userId, Guid chapterId, int pageIndex, bool completed, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Include(c => c.ActiveArtifact!).ThenInclude(a => a.ChapterLinks)
            .FirstOrDefaultAsync(c => c.Id == chapterId, ct)
            ?? throw new InvalidOperationException("Chapter not found.");

        var length = chapter.ActiveArtifact is null
            ? 0
            : (await ComputeWindowAsync(chapter.ActiveArtifact, chapter.Id, ct)).Length;

        var clamped = length > 0 ? Math.Clamp(pageIndex, 0, length - 1) : Math.Max(0, pageIndex);
        var isComplete = completed || (length > 0 && clamped >= length - 1);

        var progress = await db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);
        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId, ChapterId = chapterId };
            db.ReadingProgress.Add(progress);
        }

        progress.PageIndex = clamped;
        progress.Completed = isComplete;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetChapterReadAsync(
        Guid userId, Guid chapterId, bool read, CancellationToken ct = default)
    {
        var progress = await db.ReadingProgress
            .FirstOrDefaultAsync(p => p.UserId == userId && p.ChapterId == chapterId, ct);

        if (!read)
        {
            // Unread = no progress row at all, so it reads as never-opened everywhere (Started/pageIndex/
            // scroll all fall back to their empty defaults).
            if (progress is not null)
            {
                db.ReadingProgress.Remove(progress);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        if (progress is null)
        {
            progress = new ReadingProgress { UserId = userId, ChapterId = chapterId };
            db.ReadingProgress.Add(progress);
        }

        progress.Completed = true;
        progress.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ReaderNeighbors> GetNeighborsAsync(Guid chapterId, CancellationToken ct = default)
    {
        var chapter = await db.Chapters
            .Where(c => c.Id == chapterId)
            .Select(c => new { c.SeriesId, c.Language, c.Series.SortMode })
            .FirstOrDefaultAsync(ct);
        if (chapter is null)
        {
            return new ReaderNeighbors(null, null);
        }

        // Only downloaded chapters in the same language are navigable. Order in memory — SQLite can't
        // ORDER BY the decimal NumberSort reliably.
        var siblings = await db.Chapters
            .Where(c => c.SeriesId == chapter.SeriesId && c.Language == chapter.Language && c.ActiveArtifactId != null)
            .Select(c => new { c.Id, c.Number, c.NumberSort, c.NumberKey, c.VolumeSort })
            .ToListAsync(ct);

        var ordered = (chapter.SortMode == ChapterSortMode.VolumeThenChapter
                ? siblings
                    .OrderBy(c => c.VolumeSort ?? decimal.MaxValue)
                    .ThenBy(c => c.Number == null ? 0 : 1)
                    .ThenBy(c => c.NumberSort ?? decimal.MaxValue)
                    .ThenBy(c => c.NumberKey, StringComparer.Ordinal)
                : siblings
                    .OrderBy(c => c.NumberSort ?? decimal.MaxValue)
                    .ThenBy(c => c.NumberKey, StringComparer.Ordinal))
            .ToList();

        var i = ordered.FindIndex(c => c.Id == chapterId);
        if (i < 0)
        {
            return new ReaderNeighbors(null, null);
        }

        return new ReaderNeighbors(
            i > 0 ? ordered[i - 1].Id : null,
            i < ordered.Count - 1 ? ordered[i + 1].Id : null);
    }

    public async Task<bool> IsReadingAsync(Guid userId, Guid seriesId, CancellationToken ct = default)
    {
        var entry = await db.SeriesReadingEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.SeriesId == seriesId, ct);
        if (entry is not null)
        {
            return !entry.Dismissed;
        }

        // No explicit entry: implicitly reading if they have progress on any chapter of the series.
        return await db.ReadingProgress.AnyAsync(p => p.UserId == userId && p.Chapter.SeriesId == seriesId, ct);
    }

    public async Task SetReadingAsync(Guid userId, Guid seriesId, bool dismissed, CancellationToken ct = default)
    {
        var entry = await db.SeriesReadingEntries
            .FirstOrDefaultAsync(e => e.UserId == userId && e.SeriesId == seriesId, ct);
        if (entry is null)
        {
            entry = new SeriesReadingEntry { UserId = userId, SeriesId = seriesId };
            db.SeriesReadingEntries.Add(entry);
        }

        entry.Dismissed = dismissed;
        entry.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ContinueReadingItem>> GetContinueReadingAsync(
        Guid userId, MediaKind? kind, int limit, CancellationToken ct = default)
    {
        var entries = await db.SeriesReadingEntries
            .Where(e => e.UserId == userId)
            .Select(e => new { e.SeriesId, e.Dismissed, e.UpdatedAt })
            .ToListAsync(ct);
        var dismissed = entries.Where(e => e.Dismissed).Select(e => e.SeriesId).ToHashSet();
        var added = entries.Where(e => !e.Dismissed).Select(e => e.SeriesId).ToHashSet();

        // The user's progress (any chapter), used both to seed candidates and to find the next chapter.
        var progress = await db.ReadingProgress
            .Where(p => p.UserId == userId)
            .Select(p => new
            {
                p.ChapterId,
                p.PageIndex,
                p.Completed,
                p.UpdatedAt,
                SeriesId = p.Chapter.SeriesId,
                Language = p.Chapter.Language,
            })
            .ToListAsync(ct);

        var candidates = progress.Select(p => p.SeriesId).Concat(added).Distinct().Where(id => !dismissed.Contains(id)).ToHashSet();

        // Null kind = the user has opted into a combined Home across both libraries. Otherwise scope to the
        // library they're in, like every other page — reading progress is per-user but the series it points
        // at belongs to exactly one library.
        if (kind is { } scope && candidates.Count > 0)
        {
            candidates = (await db.Series
                    .Where(s => candidates.Contains(s.Id) && s.Kind == scope)
                    .Select(s => s.Id)
                    .ToListAsync(ct))
                .ToHashSet();
        }

        if (candidates.Count == 0)
        {
            return [];
        }

        var chapters = await db.Chapters
            .Where(c => candidates.Contains(c.SeriesId) && c.ActiveArtifactId != null)
            .Select(c => new
            {
                c.Id,
                c.SeriesId,
                c.Language,
                c.NumberSort,
                c.NumberKey,
                c.Number,
                c.Volume,
                c.VolumeSort,
                PageCount = c.ActiveArtifact!.PageCount,
            })
            .ToListAsync(ct);

        var seriesInfo = await db.Series
            .Where(s => candidates.Contains(s.Id))
            .Select(s => new { s.Id, s.Title, s.CoverPath, s.SortMode })
            .ToDictionaryAsync(s => s.Id, ct);

        var progressByChapter = progress.ToDictionary(p => p.ChapterId, p => (p.PageIndex, p.Completed));
        var entryUpdatedBySeries = entries.ToDictionary(e => e.SeriesId, e => e.UpdatedAt);

        var items = new List<ContinueReadingItem>();
        foreach (var seriesId in candidates)
        {
            if (!seriesInfo.TryGetValue(seriesId, out var info))
            {
                continue;
            }

            var seriesProgress = progress.Where(p => p.SeriesId == seriesId).ToList();

            // Read in the language of the most-recent progress; else the most-downloaded language.
            var latest = seriesProgress.OrderByDescending(p => p.UpdatedAt).FirstOrDefault();
            var lang = latest?.Language
                ?? chapters.Where(c => c.SeriesId == seriesId)
                    .GroupBy(c => c.Language).OrderByDescending(g => g.Count())
                    .Select(g => g.Key).FirstOrDefault()
                ?? "en";

            var seriesLangChapters = chapters.Where(c => c.SeriesId == seriesId && c.Language == lang);
            var orderedLangChapters = info.SortMode == ChapterSortMode.VolumeThenChapter
                ? seriesLangChapters
                    .OrderBy(c => c.VolumeSort ?? decimal.MaxValue)
                    .ThenBy(c => c.Number == null ? 0 : 1)
                    .ThenBy(c => c.NumberSort ?? decimal.MaxValue)
                    .ThenBy(c => c.NumberKey, StringComparer.Ordinal)
                : seriesLangChapters
                    .OrderBy(c => c.NumberSort ?? decimal.MaxValue)
                    .ThenBy(c => c.NumberKey, StringComparer.Ordinal);
            var next = orderedLangChapters
                .FirstOrDefault(c => !progressByChapter.TryGetValue(c.Id, out var p) || !p.Completed);
            if (next is null)
            {
                continue; // caught up on downloaded chapters
            }

            progressByChapter.TryGetValue(next.Id, out var np);
            var lastActivity = seriesProgress.Count > 0 ? seriesProgress.Max(p => p.UpdatedAt) : DateTimeOffset.MinValue;
            if (entryUpdatedBySeries.TryGetValue(seriesId, out var entryUpdated) && entryUpdated > lastActivity)
            {
                lastActivity = entryUpdated;
            }

            items.Add(new ContinueReadingItem(
                seriesId, info.Title, info.CoverPath, next.Id, next.Number, next.Volume, lang,
                np.PageIndex, next.PageCount, lastActivity));
        }

        return items.OrderByDescending(i => i.UpdatedAt).Take(limit).ToList();
    }

    /// <summary>The chapter's page window inside its artifact. Single-chapter artifacts (the norm) map
    /// to the whole file; multi-chapter artifacts derive each chapter's length from its active release's
    /// page count, in <see cref="ArtifactChapter.Order"/>.</summary>
    private async Task<(int Offset, int Length)> ComputeWindowAsync(
        Artifact artifact, Guid chapterId, CancellationToken ct)
    {
        if (artifact.ChapterLinks.Count <= 1)
        {
            return (0, artifact.PageCount);
        }

        var ordered = artifact.ChapterLinks.OrderBy(l => l.Order).ToList();

        // Prefer the page count recorded on the link at import time; fall back to the active release's
        // count only where a link doesn't carry one (e.g. an older multi-chapter artifact).
        Dictionary<Guid, int>? releaseCounts = null;
        if (ordered.Any(l => l.PageCount is null))
        {
            var ids = ordered.Where(l => l.PageCount is null).Select(l => l.ChapterId).ToList();
            releaseCounts = await db.Chapters
                .Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, Count = c.ActiveRelease != null ? c.ActiveRelease.PageCount : null })
                .ToDictionaryAsync(x => x.Id, x => x.Count ?? 0, ct);
        }

        var offset = 0;
        foreach (var link in ordered)
        {
            var count = link.PageCount ?? releaseCounts?.GetValueOrDefault(link.ChapterId) ?? 0;
            if (link.ChapterId == chapterId)
            {
                var remaining = Math.Max(0, artifact.PageCount - offset);
                return (offset, count > 0 ? Math.Min(count, remaining) : remaining);
            }

            offset += count;
        }

        return (0, artifact.PageCount);
    }

    /// <summary>Default paged reading direction. Comics are always left-to-right, whatever their language
    /// says — the right-to-left rule is a property of Japanese/Chinese *manga*, not of the language itself,
    /// and a comic's OriginalLanguage is never a reason to flip the page order. The reader lets the user
    /// override it either way.</summary>
    private static string DeriveDirection(MediaKind kind, string? originalLanguage) =>
        kind != MediaKind.Comic && MangaLanguage.IsRightToLeft(originalLanguage) ? "rtl" : "ltr";
}
