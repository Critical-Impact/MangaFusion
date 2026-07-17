using MangaFusion.Application.Writing;
using MangaFusion.Domain.Library;
using Microsoft.Extensions.Configuration;

namespace MangaFusion.Infrastructure.Writing;

/// <summary>Chooses a chapter writer by format. Default comes from <c>Library:Format</c> (CBZ).</summary>
public sealed class ChapterWriterSelector
{
    private readonly IReadOnlyDictionary<StorageFormat, IChapterWriter> _writers;

    public ChapterWriterSelector(IEnumerable<IChapterWriter> writers, IConfiguration config)
    {
        _writers = writers.ToDictionary(w => w.Format);
        DefaultFormat = Enum.TryParse<StorageFormat>(config["Library:Format"], ignoreCase: true, out var f)
            ? f
            : StorageFormat.Cbz;
    }

    public StorageFormat DefaultFormat { get; }

    public IChapterWriter Get(StorageFormat? format = null) => _writers[format ?? DefaultFormat];
}
