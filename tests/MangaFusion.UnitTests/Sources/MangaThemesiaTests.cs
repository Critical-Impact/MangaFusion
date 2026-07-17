using System.Net;
using System.Text;
using MangaFusion.Contracts.Models;
using MangaFusion.Sources.Web.Platforms;
using MangaFusion.Sources.Web.Util;

namespace MangaFusion.UnitTests.Sources;

/// <summary>Locks the MangaThemesia platform's parsing (popular list / details incl. :contains-style
/// tsinfo rows / chapter list / #readerarea pages / ts_reader JS page fallback) against canned HTML.</summary>
public class MangaThemesiaTests
{
    private const string PopularHtml = """
        <html><body>
          <div class="listupd"><div class="bs"><div class="bsx">
            <a href="https://mt.test/manga/test-manga/" title="Test Manga"><img src="/cover.jpg"></a>
          </div></div></div>
        </body></html>
        """;

    private const string DetailsHtml = """
        <html><body>
          <div class="bigcontent">
            <h1 class="entry-title">Test Manga</h1>
            <div class="tsinfo">
              <div class="imptdt">Status <i>Ongoing</i></div>
              <div class="imptdt">Author <i>Jane Doe</i></div>
              <div class="imptdt">Artist <i>Joe Roe</i></div>
              <div class="imptdt">Type <a>Manhwa</a></div>
            </div>
            <div class="entry-content" itemprop="description"><p>Desc line.</p></div>
            <div class="mgen"><a>Action</a><a>Drama</a></div>
            <div class="thumb"><img src="/cover.jpg"></div>
            <div id="chapterlist"><ul>
              <li><a href="https://mt.test/test-manga-chapter-2/"><span class="chapternum">Chapter 2</span><span class="chapterdate">January 5, 2024</span></a></li>
              <li><a href="https://mt.test/test-manga-chapter-1/"><span class="chapternum">Chapter 1</span><span class="chapterdate">January 1, 2024</span></a></li>
            </ul></div>
          </div>
        </body></html>
        """;

    private const string ReaderHtml = """
        <html><body><div id="readerarea"><img src="/p1.jpg"><img src="/p2.jpg"></div></body></html>
        """;

    private const string JsReaderHtml = """
        <html><body>
          <div id="readerarea"></div>
          <script>ts_reader.run({"post_id":1,"sources":[{"source":"a","images":["https://mt.test/js1.jpg","https://mt.test/js2.jpg"]}]});</script>
        </body></html>
        """;

    private static FakeMangaThemesia BuildSource(HttpMessageHandler handler) => new(new SingleClientFactory(handler));

    [Fact]
    public async Task Popular_parses_list_items()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/"] = PopularHtml });

        var series = (await source.SearchAsync(new SearchQuery())).Items.Single();

        Assert.Equal("web.fake-mangathemesia", series.SourceId);
        Assert.Equal("Test Manga", series.Title);
        Assert.Equal("/manga/test-manga/", UrlUtil.DecodeId(series.SourceSeriesId));
    }

    [Fact]
    public async Task Details_reads_tsinfo_rows()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/test-manga/"] = DetailsHtml });

        var series = await source.GetSeriesAsync(UrlUtil.EncodeId("/manga/test-manga/"));

        Assert.NotNull(series);
        Assert.Equal("Test Manga", series!.Title);
        Assert.Equal("Jane Doe", series.Authors.Single());
        Assert.Equal("Joe Roe", series.Artists.Single());
        Assert.Equal(PublicationStatus.Ongoing, series.Status);
        Assert.Contains("Action", series.Tags);
        Assert.Contains("Manhwa", series.Tags); // from the "Type" row
    }

    [Fact]
    public async Task Chapters_parse_from_chapterlist_ascending()
    {
        var source = BuildSource(new RoutingHandler { ["/manga/test-manga/"] = DetailsHtml });

        var chapters = (await source.GetChaptersAsync(
            UrlUtil.EncodeId("/manga/test-manga/"), new ChapterQuery())).Items;

        Assert.Equal(["1", "2"], chapters.Select(c => c.Number));
        Assert.Equal(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), chapters[0].PublishedAt);
    }

    [Fact]
    public async Task Pages_parse_from_readerarea()
    {
        var source = BuildSource(new RoutingHandler { ["/test-manga-chapter-1/"] = ReaderHtml });

        var pages = await source.GetPagesAsync(UrlUtil.EncodeId("/test-manga-chapter-1/"));

        Assert.Equal(["https://mt.test/p1.jpg", "https://mt.test/p2.jpg"], pages.Pages.Select(p => p.Url));
    }

    [Fact]
    public async Task Pages_fall_back_to_ts_reader_script()
    {
        var source = BuildSource(new RoutingHandler { ["/js-chapter/"] = JsReaderHtml });

        var pages = await source.GetPagesAsync(UrlUtil.EncodeId("/js-chapter/"));

        Assert.Equal(["https://mt.test/js1.jpg", "https://mt.test/js2.jpg"], pages.Pages.Select(p => p.Url));
    }

    private sealed class FakeMangaThemesia(IHttpClientFactory httpClientFactory) : MangaThemesia(httpClientFactory)
    {
        public override string Name => "Fake MangaThemesia";
        public override string BaseUrl => "https://mt.test";
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
