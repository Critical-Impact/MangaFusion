using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

public class ImportScannerRecursionTests : IDisposable
{
    private readonly string _inbox = Directory.CreateTempSubdirectory("mf-import-scan-").FullName;
    private readonly ImportScanner _scanner;

    public ImportScannerRecursionTests()
    {
        var config = new ConfigurationBuilder().Build();
        var writers = new ChapterWriterSelector([new CbzChapterWriter(TestPageEncoding.Resolver), new FolderChapterWriter(TestPageEncoding.Resolver)], config);
        var chapterImporter = new ChapterFileImporter(
            null!, null!, writers, new ArtifactFileInspector(), new PdfPageExtractor(config),
            new CbrPageExtractor(), new EpubPageExtractor());
        _scanner = new ImportScanner(chapterImporter, NullLogger<ImportScanner>.Instance);
    }

    public void Dispose() => Directory.Delete(_inbox, recursive: true);

    private static async Task WriteCbzAsync(string path, int pages)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var files = new List<PageFile>();
        var tmp = Directory.CreateTempSubdirectory("mf-import-scan-src-").FullName;
        try
        {
            for (var i = 0; i < pages; i++)
            {
                var src = Path.Combine(tmp, $"{i}.jpg");
                await File.WriteAllBytesAsync(src, [0xFF, 0xD8, (byte)i]);
                files.Add(new PageFile(i, $"{i}.jpg", src));
            }

            var segments = new List<ChapterSegment> { new("1", null, null, "en", null, files) };
            await new CbzChapterWriter(TestPageEncoding.Resolver).WriteAsync(new WriteRequest(
                "x", [], [], StorageFormat.Cbz,
                Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path), segments));
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    private static async Task WriteProseEpubAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var body = "<p>" + string.Concat(Enumerable.Repeat(
            "The lantern guttered as the wind slipped under the door and the old man kept reading. ", 12)) + "</p>";
        await new EpubChapterWriter().WriteAsync(new ProseWriteRequest(
            "x", [], [], Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path),
            [new ProseChapterSegment("1", null, null, "en", body, new Dictionary<string, string>())]));
    }

    /// <summary>A light-novel volume EPUB dropped straight in the inbox root (no release subfolder) is
    /// picked up as its own release and detected as prose, with no page count (it imports as one volume
    /// chapter) — this is the shape the batch wizard needs to match a novel against MangaUpdates.</summary>
    [Fact]
    public async Task ScanInbox_detects_a_top_level_light_novel_epub_as_prose()
    {
        await WriteProseEpubAsync(Path.Combine(_inbox, "7th Time Loop - Volume 01.epub"));

        var groups = _scanner.ScanInbox(_inbox, MediaKind.LightNovel);

        var group = Assert.Single(groups);
        Assert.Equal("7th Time Loop", group.GroupTitle);
        var file = Assert.Single(group.Files);
        Assert.Equal(ChapterSourceKind.ProseEpub, file.Kind);
        Assert.Equal("1", file.ParsedVolume);
        Assert.Equal(0, file.PageCount); // prose: whole-volume chapter, no page count
    }

    [Fact]
    public async Task ScanInbox_skips_a_corrupt_cbz_without_failing_the_scan()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series", "Some Series v01.cbz"), pages: 2);
        await File.WriteAllBytesAsync(Path.Combine(_inbox, "Some Series", "Some Series v02.cbz"), [1, 2, 3]);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Equal("1", file.ParsedVolume);
    }

    [Theory]
    [InlineData("Other Name 01", "1")]
    [InlineData("Other Name v02", "2")]
    [InlineData("Other Name - Volume 3", "3")]
    public async Task ScanInbox_reads_the_volume_off_each_light_novel_file(string fileName, string expectedVolume)
    {
        await WriteProseEpubAsync(Path.Combine(_inbox, "Some Series", $"{fileName}.epub"));

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.LightNovel)).Files);

        Assert.Equal(expectedVolume, file.ParsedVolume);
        Assert.Null(file.ParsedNumber);
    }

    /// <summary>A comic release is a folder of numbered issues, not a volume. The issue number has to come
    /// off each file — the folder name carries none — so that a comic import ends up with real chapter
    /// numbers rather than a pile of unnumbered whole-volume artifacts.</summary>
    [Fact]
    public async Task ScanInbox_reads_the_issue_number_off_each_comic_file()
    {
        var releaseDir = Path.Combine(_inbox, "100 Bullets");
        await WriteCbzAsync(Path.Combine(releaseDir, "100 Bullets #017"), pages: 2);
        await WriteCbzAsync(Path.Combine(releaseDir, "100 Bullets #018"), pages: 2);

        var groups = _scanner.ScanInbox(_inbox, MediaKind.Comic);

        var group = Assert.Single(groups);
        Assert.Equal("100 Bullets", group.GroupTitle); // the "100" is the title, not an issue number
        Assert.Equal(["17", "18"], group.Files.Select(f => f.ParsedNumber));
        Assert.All(group.Files, f => Assert.Null(f.ParsedVolume));
    }

    /// <summary>The file names inside a manga release folder don't have to match the folder's name — the
    /// volume comes off whatever numbering convention each file uses.</summary>
    [Theory]
    [InlineData("Other Name 001", "1")]
    [InlineData("Other Name 01", "1")]
    [InlineData("Other Name v01", "1")]
    [InlineData("Other Name Volume 1", "1")]
    [InlineData("Volume 1", "1")]
    [InlineData("Other_Name_v03_(Digital)", "3")]
    [InlineData("Other Name 012 (2021) [Group]", "12")]
    public async Task ScanInbox_reads_the_volume_off_each_manga_file(string fileName, string expectedVolume)
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series", $"{fileName}.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Equal(expectedVolume, file.ParsedVolume);
        Assert.Null(file.ParsedNumber);
    }

    [Theory]
    [InlineData("Other Name Ch.1", "1")]
    [InlineData("Other Name Ch 007", "7")]
    [InlineData("Other Name Chapter 12.5", "12.5")]
    public async Task ScanInbox_reads_an_explicit_chapter_marker_as_the_number_not_the_volume(
        string fileName, string expectedNumber)
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series", $"{fileName}.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Equal(expectedNumber, file.ParsedNumber);
        Assert.Null(file.ParsedVolume);
    }

    [Fact]
    public async Task ScanInbox_keeps_both_volume_and_chapter_when_a_manga_file_has_both()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series", "Some_Series_v02_ch010.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Equal("2", file.ParsedVolume);
        Assert.Equal("10", file.ParsedNumber);
    }

    /// <summary>A chapter range is a multi-chapter file — guessing its first chapter would be wrong.</summary>
    [Fact]
    public async Task ScanInbox_declines_to_number_a_chapter_range()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series", "Some Series Ch.001-005.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Null(file.ParsedNumber);
        Assert.Null(file.ParsedVolume);
    }

    /// <summary>A trailing number that's part of the series' own name must not become a volume.</summary>
    [Fact]
    public async Task ScanInbox_does_not_read_a_number_in_the_series_title_as_a_volume()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Mob Psycho 100", "Mob.Psycho.100.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Null(file.ParsedVolume);
    }

    /// <summary>A bare number is the weakest volume signal — an explicit volume on the release folder wins,
    /// since numbered files inside a volume folder are more likely chapters than volumes.</summary>
    [Fact]
    public async Task ScanInbox_prefers_the_folder_s_explicit_volume_over_a_bare_file_number()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series Vol.02", "Some Series 003.cbz"), pages: 2);

        var file = Assert.Single(Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga)).Files);

        Assert.Equal("2", file.ParsedVolume);
    }

    [Fact]
    public async Task ScanInbox_groups_per_chapter_manga_files_into_one_series()
    {
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series Ch.1.cbz"), pages: 2);
        await WriteCbzAsync(Path.Combine(_inbox, "Some Series Ch.2.cbz"), pages: 2);

        var group = Assert.Single(_scanner.ScanInbox(_inbox, MediaKind.Manga));

        Assert.Equal("Some Series", group.GroupTitle);
        Assert.Equal(["1", "2"], group.Files.Select(f => f.ParsedNumber));
    }

    [Fact]
    public async Task ScanInbox_finds_a_cbz_nested_inside_a_subfolder_of_the_release_folder()
    {
        var releaseDir = Path.Combine(_inbox, "Some.Publisher-A.Series.Vol.01-Group");
        await WriteCbzAsync(Path.Combine(releaseDir, "nested", "chapter"), pages: 3);

        var groups = _scanner.ScanInbox(_inbox, MediaKind.Manga);

        var group = Assert.Single(groups);
        Assert.Equal("A Series", group.GroupTitle);
        var file = Assert.Single(group.Files);
        Assert.Equal("Some.Publisher-A.Series.Vol.01-Group", file.FolderName);
        Assert.Equal(Path.Combine("nested", "chapter.cbz"), file.FileName);
        Assert.Equal(3, file.PageCount);
    }

    [Fact]
    public async Task ScanInbox_still_only_uses_the_top_level_folder_name_for_the_series_guess()
    {
        var releaseDir = Path.Combine(_inbox, "Yen.Press-My.Great.Series.Vol.02-BitBook");
        await WriteCbzAsync(Path.Combine(releaseDir, "extras", "misleading.folder.name", "ch"), pages: 2);

        var groups = _scanner.ScanInbox(_inbox, MediaKind.Manga);

        var group = Assert.Single(groups);
        Assert.Equal("My Great Series", group.GroupTitle);
    }

    [Fact]
    public async Task ScanInbox_prefers_each_file_s_own_volume_marker_over_the_folder_s()
    {
        // A batch folder with no volume marker of its own, containing several individually-named
        // volume files — each file's own "v0N" should win over the (absent) folder-level guess.
        var releaseDir = Path.Combine(_inbox, "Some.Series-Group");
        await WriteCbzAsync(Path.Combine(releaseDir, "Some Series v01"), pages: 2);
        await WriteCbzAsync(Path.Combine(releaseDir, "Some Series v02"), pages: 2);

        var groups = _scanner.ScanInbox(_inbox, MediaKind.Manga);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(["1", "2"], group.Files.Select(f => f.ParsedVolume).OrderBy(v => v));
    }
}
