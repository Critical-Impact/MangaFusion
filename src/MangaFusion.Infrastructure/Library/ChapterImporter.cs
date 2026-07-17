using MangaFusion.Application.Library;
using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Upserts source chapters into logical chapters + releases, de-duplicating releases into
/// one logical chapter per (language, normalized number). Shared by add-to-library and the monitor.
/// Does not call SaveChanges — the caller owns the transaction. Returns the newly-created logical
/// chapters (for new-chapter notifications).</summary>
public sealed class ChapterImporter(AppDbContext db)
{
    public async Task<IReadOnlyList<Chapter>> ImportAsync(
        Series series, IReadOnlyList<SourceChapter> sourceChapters, CancellationToken ct = default)
    {
        var existingChapters = await db.Chapters
            .Where(c => c.SeriesId == series.Id)
            .ToDictionaryAsync(c => (c.Language, c.NumberKey), ct);

        var seenReleases = (await db.ChapterReleases
                .Where(r => r.Chapter.SeriesId == series.Id)
                .Select(r => new { r.SourceId, r.SourceChapterId })
                .ToListAsync(ct))
            .Select(x => (x.SourceId, x.SourceChapterId))
            .ToHashSet();

        var newChapters = new List<Chapter>();

        foreach (var sc in sourceChapters)
        {
            if (!seenReleases.Add((sc.SourceId, sc.SourceChapterId)))
            {
                continue; // release already recorded
            }

            // Pass the title as a last-resort dedup discriminator: scraped sources frequently expose
            // number-less named chapters ("Prologue", "Extra"), which would otherwise all collapse onto
            // the shared "oneshot" key and hide every one but the first (see ChapterNumber.Normalize).
            var (sort, key) = ChapterNumber.Normalize(sc.Number, title: sc.Title);
            var chapterKey = (sc.Language, key);
            if (!existingChapters.TryGetValue(chapterKey, out var chapter))
            {
                chapter = new Chapter
                {
                    SeriesId = series.Id,
                    Language = sc.Language,
                    Number = sc.Number,
                    NumberSort = sort,
                    NumberKey = key,
                    Volume = sc.Volume,
                    Title = sc.Title,
                };
                series.Chapters.Add(chapter);
                db.Chapters.Add(chapter); // force Added state (entities carry client-set Guid keys)
                existingChapters[chapterKey] = chapter;
                newChapters.Add(chapter);
            }

            var release = new ChapterRelease
            {
                SourceId = sc.SourceId,
                SourceChapterId = sc.SourceChapterId,
                ScanlationGroups = sc.ScanlationGroups.ToList(),
                GroupKey = ChapterNumber.GroupKey(sc.ScanlationGroups),
                PublishedAt = sc.PublishedAt,
                PageCount = sc.PageCount,
                IsExternal = sc.IsExternal,
                ExternalUrl = sc.ExternalUrl,
            };
            chapter.Releases.Add(release);
            db.ChapterReleases.Add(release); // force Added state
        }

        return newChapters;
    }
}
