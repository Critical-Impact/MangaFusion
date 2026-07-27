using MangaFusion.Application.Writing;
using MangaFusion.Infrastructure.Reading;
using MangaFusion.Infrastructure.Writing;

namespace MangaFusion.UnitTests.Reading;

/// <summary>End-to-end for the prose backend without a DB or the import pipeline: write an EPUB3 with
/// <see cref="EpubChapterWriter"/>, read it back with <see cref="ProseArtifactReader"/>, and assert the
/// spine round-trips, the mandatory server-side sanitization strips a <c>&lt;script&gt;</c>, and inline
/// images survive and stream back.</summary>
public sealed class ProsePipelineTests : IDisposable
{
    // 1x1 transparent PNG.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _dir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), $"prose-test-{Guid.NewGuid():N}")).FullName;

    [Fact]
    public async Task Write_then_read_round_trips_and_sanitizes()
    {
        var imagePath = Path.Combine(_dir, "pic.png");
        await File.WriteAllBytesAsync(imagePath, OnePixelPng);

        var html =
            "<h1>Chapter One</h1>" +
            "<p>The quick brown fox jumps over the lazy dog.</p>" +
            "<script>alert('xss')</script>" +
            "<p onclick=\"steal()\">Second paragraph with <img src=\"pic.png\" alt=\"art\"/> inline art.</p>";

        var request = new ProseWriteRequest(
            SeriesTitle: "Test Novel",
            Authors: ["Jane Author"],
            Genres: ["Fantasy"],
            TargetDirectory: _dir,
            FileBaseName: "test-novel-vol1",
            Segments:
            [
                new ProseChapterSegment(
                    Number: "1", Volume: null, Title: "Chapter One", Language: "en",
                    Html: html,
                    Images: new Dictionary<string, string> { ["pic.png"] = imagePath }),
            ]);

        var result = await new EpubChapterWriter().WriteAsync(request);

        Assert.True(File.Exists(result.Path));
        Assert.EndsWith(".epub", result.Path);
        Assert.Equal(1, result.ChapterCount);

        var reader = new ProseArtifactReader();
        var content = await reader.ReadBookAsync(result.Path);

        Assert.NotNull(content);
        // Sanitization: no script, no inline event handler survives.
        Assert.DoesNotContain("<script", content!.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", content.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", content.Html, StringComparison.OrdinalIgnoreCase);
        // Prose survives.
        Assert.Contains("quick brown fox", content.Html);
        Assert.True(content.WordCount > 5);

        // The image survived and streams back with the right content type.
        Assert.Single(content.ImageContentTypes);
        var (imageName, contentType) = content.ImageContentTypes.First();
        Assert.Equal("image/png", contentType);

        var image = await reader.OpenImageAsync(result.Path, imageName);
        Assert.NotNull(image);
        using var ms = new MemoryStream();
        await image!.Stream.CopyToAsync(ms);
        Assert.Equal(OnePixelPng, ms.ToArray());
    }

    [Fact]
    public async Task Whole_book_read_concatenates_every_spine_section_in_order()
    {
        // A multi-section EPUB (the shape a real volume has: cover/section-1/section-2/…) is one chapter;
        // the reader stitches the whole spine into one continuous document rather than one section.
        var request = new ProseWriteRequest(
            SeriesTitle: "Omnibus",
            Authors: [],
            Genres: [],
            TargetDirectory: _dir,
            FileBaseName: "omnibus",
            Segments:
            [
                new ProseChapterSegment("1", "1", "One", "en", "<p>First section body.</p>",
                    new Dictionary<string, string>()),
                new ProseChapterSegment("2", "1", "Two", "en", "<p>Second section body.</p>",
                    new Dictionary<string, string>()),
            ]);

        var result = await new EpubChapterWriter().WriteAsync(request);

        var book = await new ProseArtifactReader().ReadBookAsync(result.Path);
        Assert.NotNull(book);
        Assert.Contains("First section body", book!.Html);
        Assert.Contains("Second section body", book.Html);
        // Each spine section is wrapped as a stable anchor the reader tracks reading position against.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(book.Html, "class=\"prose-section\"").Count);
        // Reading order preserved: section one appears before section two in the concatenated document.
        Assert.True(book.Html.IndexOf("First section", StringComparison.Ordinal)
            < book.Html.IndexOf("Second section", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
