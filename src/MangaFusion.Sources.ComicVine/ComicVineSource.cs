using System.Globalization;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.ComicVine.Http;
using MangaFusion.Sources.ComicVine.Mapping;

namespace MangaFusion.Sources.ComicVine;

/// <summary>The ComicVine source: comic metadata (volumes) and issue listings.
///
/// Deliberately <b>not</b> an <see cref="IDownloadSource"/> — ComicVine hosts no page images, so comics
/// only ever enter the library through a local/manual import. It does implement <see cref="IChapterSource"/>
/// even so: the issue list is what gives an imported CBZ its real issue number, title and release date,
/// and lets the import wizard match filenames against a real feed. The monitor won't auto-download from
/// it, because it only plans downloads for sources that advertise <see cref="SourceCapabilities.Download"/>.</summary>
public sealed class ComicVineSource(ComicVineApiClient api) : IMetadataSource, IChapterSource, ICredentialedSource
{
    public string Id => ComicVineConstants.SourceId;

    public string DisplayName => ComicVineConstants.DisplayName;

    public SourceCapabilities Capabilities =>
        SourceCapabilities.Metadata | SourceCapabilities.Chapters | SourceCapabilities.RequiresAuth;

    public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Comic];

    public IReadOnlyList<CredentialField> CredentialFields =>
    [
        new(ComicVineConstants.ApiKeyField, "API Key", Secret: true),
    ];

    public async Task<PagedResult<SourceSeries>> SearchAsync(
        SearchQuery query, CancellationToken ct = default)
    {
        var envelope = await api.SearchVolumesAsync(query, ct);
        var items = envelope?.Results?.Select(ComicVineMapper.ToSeries).ToList() ?? [];

        return new PagedResult<SourceSeries>(
            items,
            envelope?.NumberOfTotalResults ?? items.Count,
            envelope?.Limit ?? query.Limit,
            envelope?.Offset ?? query.Offset);
    }

    public async Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default)
    {
        var dto = await api.GetVolumeAsync(sourceSeriesId, ct);
        return dto is null ? null : ComicVineMapper.ToSeries(dto);
    }

    /// <summary>ComicVine has no global tag registry to sync — its "tags" (publishers, characters, teams,
    /// concepts) only exist as credits on a volume. Comic tags therefore accrete from imported series
    /// instead of being pre-seeded, which the DB-backed library tag list already handles.</summary>
    public Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceTag>>([]);

    public async Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceSeriesId, ChapterQuery query, CancellationToken ct = default)
    {
        var requested = query.Limit <= 0 ? ComicVineConstants.MaxPageSize : query.Limit;
        var envelope = await api.GetIssuesAsync(sourceSeriesId, requested, query.Offset, ct);

        var items = (envelope?.Results ?? []).Select(ComicVineMapper.ToChapter).ToList();

        // ComicVine's sort=issue_number:asc is lexicographic — it returns #1, #10, #11, #2 — so order the
        // page ourselves. Non-numeric numbers (annuals, specials) sort last, then by their raw string.
        items = query.Order == ChapterOrder.ChapterDescending
            ? [.. items.OrderByDescending(IssueSortKey).ThenByDescending(c => c.Number)]
            : [.. items.OrderBy(IssueSortKey).ThenBy(c => c.Number)];

        // The caller pages until it has Total items, and advances by the page size actually served — so
        // both must be ComicVine's, not what we asked for. Reporting items.Count as the total would make a
        // >100-issue volume look complete after its first page.
        return new PagedResult<SourceChapter>(
            items,
            envelope?.NumberOfTotalResults ?? items.Count,
            envelope?.Limit is > 0 ? envelope.Limit : Math.Min(requested, ComicVineConstants.MaxPageSize),
            envelope?.Offset ?? query.Offset);
    }

    private static decimal IssueSortKey(SourceChapter chapter) =>
        decimal.TryParse(chapter.Number, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : decimal.MaxValue;

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        if (!await api.HasApiKeyAsync(ct))
        {
            return false;
        }

        try
        {
            // Cheapest possible authenticated call: a rejected key comes back as an envelope error, which
            // the client surfaces as ComicVineApiException.
            await api.SearchVolumesAsync(new SearchQuery { Text = "batman", Limit = 1 }, ct);
            return true;
        }
        catch (ComicVineApiException)
        {
            return false;
        }
    }
}
