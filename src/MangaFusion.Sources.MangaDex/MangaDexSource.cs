using System.Net.Http.Json;
using MangaFusion.Contracts.Models;
using MangaFusion.Contracts.Sources;
using MangaFusion.Sources.MangaDex.Auth;
using MangaFusion.Sources.MangaDex.Http;
using MangaFusion.Sources.MangaDex.Mapping;

namespace MangaFusion.Sources.MangaDex;

/// <summary>The MangaDex source: metadata, chapter listing, and page resolution, backed by the
/// authenticated MangaDex API.</summary>
public sealed class MangaDexSource(
    MangaDexApiClient api, MangaDexTokenProvider tokens, IHttpClientFactory httpFactory)
    : IMetadataSource, IChapterSource, IDownloadSource, ICredentialedSource
{
    public string Id => MangaDexConstants.SourceId;

    public string DisplayName => MangaDexConstants.DisplayName;

    public SourceCapabilities Capabilities =>
        SourceCapabilities.Metadata | SourceCapabilities.Chapters |
        SourceCapabilities.Download | SourceCapabilities.RequiresAuth;

    public IReadOnlyList<MediaKind> SupportedKinds => [MediaKind.Manga];

    public IReadOnlyList<CredentialField> CredentialFields =>
    [
        new("clientId", "Client ID", Secret: false),
        new("clientSecret", "Client Secret", Secret: true),
        new("username", "Username", Secret: false),
        new("password", "Password", Secret: true),
    ];

    public async Task<PagedResult<SourceSeries>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        var dto = await api.SearchMangaAsync(query, ct);
        var items = dto?.Data.Select(MangaDexMapper.ToSeries).ToList() ?? [];
        return new PagedResult<SourceSeries>(items, dto?.Total ?? 0, dto?.Limit ?? query.Limit, dto?.Offset ?? query.Offset);
    }

    public async Task<SourceSeries?> GetSeriesAsync(string sourceSeriesId, CancellationToken ct = default)
    {
        var dto = await api.GetMangaAsync(sourceSeriesId, ct);
        return dto is null ? null : MangaDexMapper.ToSeries(dto);
    }

    public async Task<IReadOnlyList<SourceTag>> GetTagsAsync(CancellationToken ct = default)
    {
        var dto = await api.GetTagsAsync(ct);
        return dto?.Data.Select(MangaDexMapper.ToTag).ToList() ?? [];
    }

    public async Task<PagedResult<SourceChapter>> GetChaptersAsync(
        string sourceSeriesId, ChapterQuery query, CancellationToken ct = default)
    {
        var dto = await api.GetChapterFeedAsync(sourceSeriesId, query, ct);
        var items = (dto?.Data ?? [])
            .Select(MangaDexMapper.ToChapter)
            .Where(c => query.IncludeExternal || !c.IsExternal)
            .ToList();
        return new PagedResult<SourceChapter>(items, dto?.Total ?? 0, dto?.Limit ?? query.Limit, dto?.Offset ?? query.Offset);
    }

    public async Task<SourcePageSet> GetPagesAsync(
        string sourceChapterId, PageQuality quality = PageQuality.Original, CancellationToken ct = default)
    {
        var dto = await api.GetAtHomeAsync(sourceChapterId, ct)
            ?? throw new InvalidOperationException($"No @Home server available for chapter '{sourceChapterId}'.");

        var segment = quality == PageQuality.DataSaver ? "data-saver" : "data";
        var files = quality == PageQuality.DataSaver ? dto.Chapter.DataSaver : dto.Chapter.Data;

        var pages = files
            .Select((file, index) => new SourcePage(
                index,
                $"{dto.BaseUrl}/{segment}/{dto.Chapter.Hash}/{file}",
                file))
            .ToList();

        return new SourcePageSet
        {
            SourceChapterId = sourceChapterId,
            Pages = pages,
            Quality = quality,
            ReportAsync = ReportAsync,
        };
    }

    /// <summary>Reports a page fetch outcome to the MangaDex@Home network, as client guidelines ask.</summary>
    private async Task ReportAsync(PageReport report, CancellationToken ct)
    {
        var client = httpFactory.CreateClient(MangaDexConstants.ReportClient);
        var payload = new
        {
            url = report.Url,
            success = report.Success,
            cached = report.Cached,
            bytes = report.Bytes,
            duration = (int)report.Duration.TotalMilliseconds,
        };
        await client.PostAsJsonAsync(MangaDexConstants.ReportEndpoint, payload, ct);
    }

    public Task<bool> ValidateCredentialsAsync(CancellationToken ct = default) =>
        tokens.ValidateStoredAsync(ct);
}
