using System.IO.Compression;
using System.Text;
using MangaFusion.Infrastructure.Library;

namespace MangaFusion.UnitTests.Library;

public class EpubPageExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mf-epub-{Guid.NewGuid():N}");

    public EpubPageExtractorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task Extracts_pages_in_spine_order_from_image_wrapped_xhtml()
    {
        var path = Path.Combine(_root, "comic.epub");
        WriteImageBasedEpub(path, pageCount: 3);
        var extractor = new EpubPageExtractor();

        Assert.Equal(3, extractor.CountPages(path));

        var destDir = Path.Combine(_root, "out");
        var pages = await extractor.ExtractPagesAsync(path, destDir, CancellationToken.None);

        Assert.Equal(3, pages.Count);
        for (var i = 0; i < 3; i++)
        {
            var bytes = await File.ReadAllBytesAsync(pages[i]);
            Assert.Equal([0xFF, 0xD8, (byte)i], bytes); // spine order must match page index
        }
    }

    [Fact]
    public void Rejects_epub_with_reflowable_text()
    {
        var path = Path.Combine(_root, "novel.epub");
        WriteTextEpub(path);
        var extractor = new EpubPageExtractor();

        var ex = Assert.Throws<InvalidOperationException>(() => extractor.CountPages(path));
        Assert.Contains("reflowable text", ex.Message);
    }

    [Fact]
    public void Rejects_drm_protected_epub()
    {
        var path = Path.Combine(_root, "drm.epub");
        WriteImageBasedEpub(path, pageCount: 1, includeEncryption: true);
        var extractor = new EpubPageExtractor();

        var ex = Assert.Throws<InvalidOperationException>(() => extractor.CountPages(path));
        Assert.Contains("DRM", ex.Message);
    }

    /// <summary>A minimal fixed-layout comic EPUB: one XHTML "page" per spine item, each wrapping a
    /// single full-page &lt;img&gt;. Page N's image byte content is [0xFF, 0xD8, N] so tests can assert
    /// on both content and spine ordering.</summary>
    private static void WriteImageBasedEpub(string path, int pageCount, bool includeEncryption = false)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        if (includeEncryption)
        {
            WriteEntry(zip, "META-INF/encryption.xml", """
                <?xml version="1.0"?>
                <encryption xmlns="urn:oasis:names:tc:opendocument:xmlns:container"/>
                """);
        }

        var manifestItems = new StringBuilder();
        var spineItems = new StringBuilder();
        for (var i = 0; i < pageCount; i++)
        {
            manifestItems.AppendLine($"""<item id="page{i}" href="page{i}.xhtml" media-type="application/xhtml+xml"/>""");
            manifestItems.AppendLine($"""<item id="img{i}" href="images/page{i}.jpg" media-type="image/jpeg"/>""");
            spineItems.AppendLine($"""<itemref idref="page{i}"/>""");

            WriteEntry(zip, $"OEBPS/page{i}.xhtml", $"""
                <?xml version="1.0"?>
                <html xmlns="http://www.w3.org/1999/xhtml">
                  <body><img src="images/page{i}.jpg" alt="Page {i + 1}"/></body>
                </html>
                """);

            WriteEntryBytes(zip, $"OEBPS/images/page{i}.jpg", [0xFF, 0xD8, (byte)i]);
        }

        WriteEntry(zip, "OEBPS/content.opf", $"""
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <manifest>
                {manifestItems}
              </manifest>
              <spine>
                {spineItems}
              </spine>
            </package>
            """);
    }

    private static void WriteTextEpub(string path)
    {
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "META-INF/container.xml", """
            <?xml version="1.0"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """);

        WriteEntry(zip, "OEBPS/chapter1.xhtml", """
            <?xml version="1.0"?>
            <html xmlns="http://www.w3.org/1999/xhtml">
              <body>
                <p>It was the best of times, it was the worst of times, it was the age of wisdom, it
                was the age of foolishness, it was the epoch of belief, it was the epoch of incredulity.</p>
              </body>
            </html>
            """);

        WriteEntry(zip, "OEBPS/content.opf", """
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="id">
              <manifest>
                <item id="chapter1" href="chapter1.xhtml" media-type="application/xhtml+xml"/>
              </manifest>
              <spine>
                <itemref idref="chapter1"/>
              </spine>
            </package>
            """);
    }

    private static void WriteEntry(ZipArchive zip, string name, string content) =>
        WriteEntryBytes(zip, name, Encoding.UTF8.GetBytes(content));

    private static void WriteEntryBytes(ZipArchive zip, string name, byte[] bytes)
    {
        var entry = zip.CreateEntry(name);
        using var entryStream = entry.Open();
        entryStream.Write(bytes);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
