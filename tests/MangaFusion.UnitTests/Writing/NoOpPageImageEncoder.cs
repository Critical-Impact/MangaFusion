using MangaFusion.Application.Writing;

namespace MangaFusion.UnitTests.Writing;

/// <summary>Always declines — keeps writer-plumbing tests (zip/ComicInfo/hashing) decoupled from codec
/// behavior, since that's covered separately by the encoder/resolver's own tests.</summary>
public sealed class NoOpPageImageEncoder : IPageImageEncoder
{
    // Enabled: it stands in for an active encoder that simply declines every page, not a disabled one —
    // so writer/reencoder plumbing under test still runs its full path.
    public bool Enabled => true;

    public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
        Task.FromResult<EncodedPage?>(null);
}
