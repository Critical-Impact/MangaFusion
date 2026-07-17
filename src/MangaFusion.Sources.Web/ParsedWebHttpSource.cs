using AngleSharp.Dom;
using MangaFusion.Sources.Web.Models;
using MangaFusion.Sources.Web.Parsing;

namespace MangaFusion.Sources.Web;

/// <summary>A <see cref="WebHttpSource"/> whose list/chapter parsing is driven by CSS selectors — the
/// C# port of Tachiyomi's <c>ParsedHttpSource</c>. Subclasses (typically a <c>SourcePlatform</c>)
/// supply selectors + per-element mappers instead of whole parse methods. Page-list parsing stays open
/// (sites vary too much) — override <see cref="WebHttpSource.PageListParse"/>.</summary>
public abstract class ParsedWebHttpSource(IHttpClientFactory httpClientFactory)
    : WebHttpSource(httpClientFactory)
{
    // ---- Popular -----------------------------------------------------------------------------
    protected abstract string PopularMangaSelector();
    protected abstract WebManga PopularMangaFromElement(IElement element);
    protected abstract string? PopularMangaNextPageSelector();

    protected override MangasPage PopularParse(IDocument document) =>
        ParseMangaList(document, PopularMangaSelector(), PopularMangaFromElement, PopularMangaNextPageSelector());

    // ---- Latest ------------------------------------------------------------------------------
    protected abstract string LatestUpdatesSelector();
    protected abstract WebManga LatestUpdatesFromElement(IElement element);
    protected abstract string? LatestUpdatesNextPageSelector();

    protected override MangasPage LatestParse(IDocument document) =>
        ParseMangaList(document, LatestUpdatesSelector(), LatestUpdatesFromElement, LatestUpdatesNextPageSelector());

    // ---- Search ------------------------------------------------------------------------------
    protected abstract string SearchMangaSelector();
    protected abstract WebManga SearchMangaFromElement(IElement element);
    protected abstract string? SearchMangaNextPageSelector();

    protected override MangasPage SearchParse(IDocument document) =>
        ParseMangaList(document, SearchMangaSelector(), SearchMangaFromElement, SearchMangaNextPageSelector());

    // ---- Chapters ----------------------------------------------------------------------------
    protected abstract string ChapterListSelector();
    protected abstract WebChapter ChapterFromElement(IElement element);

    protected override IReadOnlyList<WebChapter> ChapterListParse(IDocument document) =>
        document.Select(ChapterListSelector()).Select(ChapterFromElement).ToList();

    private static MangasPage ParseMangaList(
        IDocument document, string selector, Func<IElement, WebManga> fromElement, string? nextSelector)
    {
        var mangas = document.Select(selector).Select(fromElement).ToList();
        var hasNext = nextSelector is { Length: > 0 } && document.SelectFirst(nextSelector) is not null;
        return new MangasPage(mangas, hasNext);
    }
}
