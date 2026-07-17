using MangaFusion.Application.Writing;

namespace MangaFusion.UnitTests.Writing;

/// <summary>Always declines — keeps writer-plumbing tests (zip/ComicInfo/hashing) decoupled from codec
/// behavior, since that's covered separately by the encoder/resolver's own tests.</summary>
public sealed class NoOpPageImageEncoder : IPageImageEncoder
{
    public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
        Task.FromResult<EncodedPage?>(null);
}
