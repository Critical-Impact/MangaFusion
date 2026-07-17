using MangaFusion.Contracts.Models;

namespace MangaFusion.Contracts.Sources;

/// <summary>A source that can list a series' chapters.</summary>
public interface IChapterSource : ISource
{
    Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceSeriesId, ChapterQuery query, CancellationToken ct = default);
}
