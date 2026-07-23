using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Library;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Configuration;

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
        _scanner = new ImportScanner(chapterImporter);
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

    /// <summary>A comic release is a folder of numbered issues, not a volume. The issue number has to come
    /// off each file — the folder name carries none — so that a comic import ends up with real chapter
    /// numbers rather than a pile of unnumbered whole-volume artifacts.</summary>
    [Fact]
    public async Task ScanInbox_reads_the_issue_number_off_each_comic_file()
    {
        var releaseDir = Path.Combine(_inbox, "100 Bullets");
        await WriteCbzAsync(Path.Combine(releaseDir, "100 Bullets #017"), pages: 2);
        await WriteCbzAsync(Path.Combine(releaseDir, "100 Bullets #018"), pages: 2);

        var groups = _scanner.ScanInbox(_inbox);

        var group = Assert.Single(groups);
        Assert.Equal("100 Bullets", group.GroupTitle); // the "100" is the title, not an issue number
        Assert.Equal(["17", "18"], group.Files.Select(f => f.ParsedNumber));
        Assert.All(group.Files, f => Assert.Null(f.ParsedVolume));
    }

    [Fact]
    public async Task ScanInbox_finds_a_cbz_nested_inside_a_subfolder_of_the_release_folder()
    {
        var releaseDir = Path.Combine(_inbox, "Some.Publisher-A.Series.Vol.01-Group");
        await WriteCbzAsync(Path.Combine(releaseDir, "nested", "chapter"), pages: 3);

        var groups = _scanner.ScanInbox(_inbox);

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

        var groups = _scanner.ScanInbox(_inbox);

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

        var groups = _scanner.ScanInbox(_inbox);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(["1", "2"], group.Files.Select(f => f.ParsedVolume).OrderBy(v => v));
    }
}
