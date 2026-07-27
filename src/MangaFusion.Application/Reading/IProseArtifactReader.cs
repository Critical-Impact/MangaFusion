namespace MangaFusion.Application.Reading;

/// <summary>A prose book's readable content: sanitized body <paramref name="Html"/> (server-side
/// sanitized — safe for the client to render via <c>innerHTML</c>), the content type of each inline image
/// keyed by the stable name its surviving <c>&lt;img src&gt;</c> now carries, and an estimated
/// <paramref name="WordCount"/> for the reading-time header.</summary>
public sealed record ProseChapterContent(
    string Html,
    IReadOnlyDictionary<string, string> ImageContentTypes,
    int WordCount);

/// <summary>Reads a stored EPUB3 prose artifact for the text reader. Parallel to
/// <see cref="IArtifactReader"/> (which returns one page image at a time): prose returns the whole
/// book's HTML at once, so it isn't a <c>StorageFormat</c> branch of the image reader. A prose artifact
/// is one whole volume = one chapter, so the entire spine (cover, sections, illustration plates) is read
/// and concatenated into a single continuous document — this is what lets a stored-as-is source EPUB be
/// rendered faithfully (text and full-page images interleaved) rather than flattened at import.</summary>
public interface IProseArtifactReader
{
    /// <summary>Reads the whole EPUB spine into one sanitized, continuous HTML document + word count.
    /// Surviving <c>&lt;img src&gt;</c> attributes are rewritten to the bare stable image name the caller
    /// turns into an image URL.</summary>
    Task<ProseChapterContent?> ReadBookAsync(string absolutePath, CancellationToken ct = default);

    /// <summary>Opens one inline image's bytes by the stable name returned in
    /// <see cref="ProseChapterContent.ImageContentTypes"/>, or null if it isn't in the artifact.</summary>
    Task<PageContent?> OpenImageAsync(string absolutePath, string imageName, CancellationToken ct = default);
}
