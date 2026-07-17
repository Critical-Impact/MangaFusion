using System.Net;
using System.Text;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.Web.Platforms;
using MangaFusion.Sources.Web.Util;

namespace MangaFusion.UnitTests.Sources;

/// <summary>Locks the Madara platform's parsing (popular / details incl. :contains status+type /
/// inline chapter list / page list) against canned HTML, through a concrete test subclass.</summary>
public class MadaraTests
{
    private const string PopularHtml = """
        <html><body>
          <div class="page-item-detail manga">
            <div class="post-title"><h3><a href="https://fake.test/manga/test-manga/">Test Manga</a></h3></div>
            <img src="/covers/test.jpg">
          </div>
        </body></html>
        """;

    private const string DetailsHtml = """
        <html><body>
          <div class="post-title"><h1>Test Manga</h1></div>
          <div class="author-content"><a>Jane Author</a></div>
          <div class="artist-content"><a>Joe Artist</a></div>
          <div class="summary_image"><img src="/covers/test.jpg"></div>
          <div class="description-summary"><div class="summary__content"><p>Line one.</p><p>Line two.</p></div></div>
          <div class="genres-content"><a>Action</a><a>Comedy</a></div>
          <div class="post-content_item"><div class="summary-heading"><h5>Status</h5></div><div class="summary-content">Ongoing</div></div>
          <div class="post-content_item"><h5>Type</h5><div class="summary-content">Manhwa</div></div>
          <ul>
            <li class="wp-manga-chapter"><a href="https://fake.test/manga/test-manga/chapter-2/">Chapter 2</a><span class="chapter-release-date">August 5, 2024</span></li>
            <li class="wp-manga-chapter"><a href="https://fake.test/manga/test-manga/chapter-1/">Chapter 1</a><span class="chapter-release-date">August 1, 2024</span></li>
          </ul>
        </body></html>
        """;

    private const string ChapterOneHtml = """
        <html><body>
          <div class="reading-content">
            <div class="page-break"><img src="/pages/1.jpg"></div>
            <div class="page-break"><img data-src="https://fake.test/pages/2.jpg" src="/lazy.gif"></div>
          </div>
        </body></html>
        """;

    private static FakeMadara BuildSource(HttpMessageHandler handler) => new(new SingleClientFactory(handler));

    [Fact]
    public async Task Popular_parses_entries()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/"] = PopularHtml });

        var result = await source.SearchAsync(new SearchQuery());

        var series = Assert.Single(result.Items);
        Assert.Equal("web.fake-madara", series.SourceId);
        Assert.Equal("Test Manga", series.Title);
        Assert.Equal("/manga/test-manga/", UrlUtil.DecodeId(series.SourceSeriesId));
        Assert.Equal("https://fake.test/covers/test.jpg", series.CoverUrl);
    }

    [Fact]
    public async Task Details_parses_fields_including_contains_status_and_type()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/test-manga/"] = DetailsHtml });

        var series = await source.GetSeriesAsync(UrlUtil.EncodeId("/manga/test-manga/"));

        Assert.NotNull(series);
        Assert.Equal("Test Manga", series!.Title);
        Assert.Equal("Jane Author", series.Authors.Single());
        Assert.Equal("Joe Artist", series.Artists.Single());
        Assert.Equal("Line one.\n\nLine two.", series.Description);
        Assert.Equal(PublicationStatus.Ongoing, series.Status);       // from :contains(Status) sibling
        Assert.Contains("Action", series.Tags);
        Assert.Contains("Manhwa", series.Tags);                        // from :contains(Type) summary-content
    }

    [Fact]
    public async Task Chapters_parse_from_inline_list_ordered_ascending()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/test-manga/"] = DetailsHtml });

        var chapters = (await source.GetChaptersAsync(
            UrlUtil.EncodeId("/manga/test-manga/"), new ChapterQuery())).Items;

        Assert.Equal(2, chapters.Count);
        Assert.Equal(["1", "2"], chapters.Select(c => c.Number));
        Assert.Equal(new DateTimeOffset(2024, 8, 1, 0, 0, 0, TimeSpan.Zero), chapters[0].PublishedAt);
        // ?style=list suffix appended to the chapter path
        Assert.Equal("/manga/test-manga/chapter-1/?style=list", UrlUtil.DecodeId(chapters[0].SourceChapterId));
    }

    [Fact]
    public async Task Pages_parse_with_lazy_src_and_absolute_urls()
    {
        var handler = new RoutingHandler
        {
            ["/manga/test-manga/"] = DetailsHtml,
            ["/manga/test-manga/chapter-1/"] = ChapterOneHtml,
        };
        var source = BuildSource(handler);
        var chapterOne = (await source.GetChaptersAsync(
            UrlUtil.EncodeId("/manga/test-manga/"), new ChapterQuery())).Items.Single(c => c.Number == "1");

        var pages = await source.GetPagesAsync(chapterOne.SourceChapterId);

        Assert.Equal(2, pages.Pages.Count);
        Assert.Equal("https://fake.test/pages/1.jpg", pages.Pages[0].Url);
        Assert.Equal("https://fake.test/pages/2.jpg", pages.Pages[1].Url); // data-src preferred over src
        Assert.Equal("https://fake.test/", pages.Headers!["Referer"]);
        Assert.True(pages.Headers.ContainsKey("User-Agent")); // browser UA for CDNs that gate on it
    }

    /// <summary>A concrete Madara for tests — points at a stable fake host.</summary>
    private sealed class FakeMadara(IHttpClientFactory httpClientFactory) : Madara(httpClientFactory)
    {
        public override string Name => "Fake Madara";
        public override string BaseUrl => "https://fake.test";
        public override string Lang => "en";
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes = new();
        public string this[string path] { set => _routes[path] = value; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_routes.TryGetValue(request.RequestUri!.AbsolutePath, out var html)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
