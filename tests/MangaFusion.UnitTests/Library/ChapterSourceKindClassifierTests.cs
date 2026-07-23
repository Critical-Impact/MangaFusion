using MangaFusion.Infrastructure.Library;

namespace MangaFusion.UnitTests.Library;

public class ChapterSourceKindClassifierTests
{
    [Theory]
    [InlineData("volume-01.cbz", ChapterSourceKind.Cbz)]
    [InlineData("volume-01.CBZ", ChapterSourceKind.Cbz)]
    [InlineData("volume-01.cbr", ChapterSourceKind.Cbr)]
    [InlineData("volume-01.pdf", ChapterSourceKind.Pdf)]
    [InlineData("volume-01.epub", ChapterSourceKind.Epub)]
    public void Classifies_recognized_extensions(string fileName, ChapterSourceKind expected) =>
        Assert.Equal(expected, ChapterSourceKindClassifier.FromFileName(fileName));

    [Theory]
    [InlineData("volume-01.zip")]
    [InlineData("volume-01.txt")]
    [InlineData("volume-01")]
    public void Returns_null_for_unrecognized_extensions(string fileName) =>
        Assert.Null(ChapterSourceKindClassifier.FromFileName(fileName));
}
