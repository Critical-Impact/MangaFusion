using System.IO.Compression;
using System.Security.Cryptography;
using MangaFusion.Infrastructure.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Library;

public class MigrationScannerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mf-migration-scan-").FullName;
    private readonly MigrationScanner _scanner =
        new(new ArtifactFileInspector(), NullLogger<MigrationScanner>.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData("Chapter25_[EN-data]_Don't hold me back, Senpai~_f60bac7d.cbz", "f60bac7d")]
    [InlineData("Chapter1_[EN-data]__db8de610.cbz", "db8de610")]
    [InlineData("no_uuid_here.cbz", null)]
    [InlineData("Chapter82_[EN-data]_Title (lewd edit)_247C238F.cbz", "247c238f")]
    public void ExtractUuidPrefix_pulls_trailing_8_hex_segment(string fileName, string? expected) =>
        Assert.Equal(expected, MigrationScanner.ExtractUuidPrefix(fileName));

    [Fact]
    public void ScanSeriesFolder_reads_ComicInfo_over_filename_for_the_number()
    {
        // Filename says "Chapter25" (dot stripped) but ComicInfo says the real value, 2.5 — the
        // scanner must trust ComicInfo, matching the old downloader's own lossy naming.
        WriteCbz("Chapter25_[EN-data]__aaaaaaaa.cbz", series: "Some Manga", number: "2.5", title: null, pages: 3);

        var files = _scanner.ScanSeriesFolder(_dir);

        var file = Assert.Single(files);
        Assert.Equal("2.5", file.Number);
        Assert.Equal("2.5", file.NumberKey);
        Assert.Equal("aaaaaaaa", file.UuidPrefix);
        Assert.Null(file.IntegrityFailureReason);
    }

    [Fact]
    public void ScanSeriesFolder_flags_pageless_files_as_integrity_failures()
    {
        WriteCbz("Chapter1_[EN-data]__11111111.cbz", series: "Some Manga", number: "1", title: null, pages: 0);

        var file = Assert.Single(_scanner.ScanSeriesFolder(_dir));

        Assert.NotNull(file.IntegrityFailureReason);
        Assert.Equal(0, file.PageCount);
    }

    [Fact]
    public void ScanSeriesFolder_flags_suspiciously_small_files_even_with_pages()
    {
        // A single tiny "page" entry — technically non-zero pages, but the file is far too small to
        // be a real chapter (mirrors the old tool's stub files, which sometimes carry a 1x1 filler).
        WriteCbz("Chapter1_[EN-data]__22222222.cbz", series: "Some Manga", number: "1", title: null, pages: 1, pageBytes: 100);

        var file = Assert.Single(_scanner.ScanSeriesFolder(_dir));

        Assert.NotNull(file.IntegrityFailureReason);
    }

    [Fact]
    public void ScanSeriesFolder_ignores_files_without_ComicInfo()
    {
        File.WriteAllText(Path.Combine(_dir, "readme.txt"), "not a chapter");
        using (ZipFile.Open(Path.Combine(_dir, "no-metadata.cbz"), ZipArchiveMode.Create))
        {
            // empty archive, no ComicInfo.xml
        }

        Assert.Empty(_scanner.ScanSeriesFolder(_dir));
    }

    [Fact]
    public void ScanInbox_diverts_a_folder_whose_files_have_no_ComicInfo()
    {
        var seriesDir = Directory.CreateDirectory(Path.Combine(_dir, "SomeSeries")).FullName;
        using (ZipFile.Open(Path.Combine(seriesDir, "ch1.cbz"), ZipArchiveMode.Create))
        {
            // a real-looking chapter file, but no ComicInfo.xml — not from the old downloader
        }

        var result = _scanner.ScanInbox(_dir);

        Assert.Empty(result.Folders);
        Assert.Equal([seriesDir], result.FoldersWithNoComicInfo);
    }

    [Fact]
    public void ScanInbox_ignores_a_folder_with_no_chapter_shaped_files_at_all()
    {
        var junkDir = Directory.CreateDirectory(Path.Combine(_dir, "Junk")).FullName;
        File.WriteAllText(Path.Combine(junkDir, "readme.txt"), "not a chapter");

        var result = _scanner.ScanInbox(_dir);

        Assert.Empty(result.Folders);
        Assert.Empty(result.FoldersWithNoComicInfo);
    }

    [Fact]
    public void ScanInbox_returns_a_folder_with_valid_ComicInfo_as_a_normal_candidate()
    {
        var seriesDir = Directory.CreateDirectory(Path.Combine(_dir, "RealSeries")).FullName;
        WriteCbzInto(seriesDir, "Chapter1_[EN-data]__aaaaaaaa.cbz", series: "Some Manga", number: "1", title: null, pages: 3);

        var result = _scanner.ScanInbox(_dir);

        var folder = Assert.Single(result.Folders);
        Assert.Equal("RealSeries", folder.FolderName);
        Assert.Empty(result.FoldersWithNoComicInfo);
    }

    private static void WriteCbzInto(
        string dir, string fileName, string series, string? number, string? title, int pages, int pageBytes = 6000)
    {
        var path = Path.Combine(dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var comicInfo = $"""
            <ComicInfo><Series>{series}</Series><Number>{number}</Number><Title>{title}</Title></ComicInfo>
            """;
        using (var entryStream = zip.CreateEntry("ComicInfo.xml").Open())
        using (var writer = new StreamWriter(entryStream))
        {
            writer.Write(comicInfo);
        }

        for (var i = 0; i < pages; i++)
        {
            using var pageStream = zip.CreateEntry($"{i:D4}.jpg", CompressionLevel.NoCompression).Open();
            pageStream.Write(RandomNumberGenerator.GetBytes(pageBytes));
        }
    }

    private void WriteCbz(
        string fileName, string series, string? number, string? title, int pages, int pageBytes = 6000)
    {
        var path = Path.Combine(_dir, fileName);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);

        var comicInfo = $"""
            <ComicInfo><Series>{series}</Series><Number>{number}</Number><Title>{title}</Title></ComicInfo>
            """;
        using (var entryStream = zip.CreateEntry("ComicInfo.xml").Open())
        using (var writer = new StreamWriter(entryStream))
        {
            writer.Write(comicInfo);
        }

        for (var i = 0; i < pages; i++)
        {
            using var pageStream = zip.CreateEntry($"{i:D4}.jpg", CompressionLevel.NoCompression).Open();
            pageStream.Write(RandomNumberGenerator.GetBytes(pageBytes));
        }
    }
}
