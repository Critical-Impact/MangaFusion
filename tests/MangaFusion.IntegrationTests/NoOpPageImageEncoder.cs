using MangaFusion.Application.Writing;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.IntegrationTests;

/// <summary>Always declines — keeps these integration tests (DB/EF/HTTP plumbing) decoupled from
/// codec behavior, which is covered separately by the encoder/resolver's own unit tests.</summary>
public sealed class NoOpPageImageEncoder : IPageImageEncoder
{
    public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
        Task.FromResult<EncodedPage?>(null);
}

public static class TestPageEncoding
{
    public static readonly PageEncodingResolver Resolver =
        new(new NoOpPageImageEncoder(), NullLogger<PageEncodingResolver>.Instance);
}
