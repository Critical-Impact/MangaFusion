using MangaFusion.Application.Writing;
using MangaFusion.Infrastructure.Writing;
using Microsoft.Extensions.Logging.Abstractions;

namespace MangaFusion.UnitTests.Writing;

public class PageEncodingResolverTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"mf-resolver-{Guid.NewGuid():N}");

    public PageEncodingResolverTests() => Directory.CreateDirectory(_dir);

    private sealed class StubEncoder(Func<string, EncodedPage?> respond) : IPageImageEncoder
    {
        public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
            Task.FromResult(respond(sourcePath));
    }

    private sealed class ThrowingEncoder : IPageImageEncoder
    {
        public Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct) =>
            throw new InvalidOperationException("boom");
    }

    private async Task<string> WriteSourceAsync(int size)
    {
        var path = Path.Combine(_dir, "src.jpg");
        await File.WriteAllBytesAsync(path, new byte[size]);
        return path;
    }

    [Fact]
    public async Task Encoder_declining_keeps_original_extension()
    {
        var src = await WriteSourceAsync(100);
        var resolver = new PageEncodingResolver(new StubEncoder(_ => null), NullLogger<PageEncodingResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new PageFile(0, "0.jpg", src), CancellationToken.None);

        Assert.Null(resolved.Bytes);
        Assert.Equal(".jpg", resolved.Extension);
    }

    [Fact]
    public async Task Encoded_result_not_smaller_falls_back_to_original()
    {
        var src = await WriteSourceAsync(100);
        var resolver = new PageEncodingResolver(
            new StubEncoder(_ => new EncodedPage(new byte[100], ".webp")), // same size, not smaller
            NullLogger<PageEncodingResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new PageFile(0, "0.jpg", src), CancellationToken.None);

        Assert.Null(resolved.Bytes);
        Assert.Equal(".jpg", resolved.Extension);
    }

    [Fact]
    public async Task Smaller_encoded_result_is_used()
    {
        var src = await WriteSourceAsync(100);
        var resolver = new PageEncodingResolver(
            new StubEncoder(_ => new EncodedPage(new byte[50], ".webp")),
            NullLogger<PageEncodingResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new PageFile(0, "0.jpg", src), CancellationToken.None);

        Assert.NotNull(resolved.Bytes);
        Assert.Equal(50, resolved.Bytes!.Length);
        Assert.Equal(".webp", resolved.Extension);
    }

    [Fact]
    public async Task Encoder_throwing_is_swallowed_and_falls_back()
    {
        var src = await WriteSourceAsync(100);
        var resolver = new PageEncodingResolver(new ThrowingEncoder(), NullLogger<PageEncodingResolver>.Instance);

        var resolved = await resolver.ResolveAsync(new PageFile(0, "0.jpg", src), CancellationToken.None);

        Assert.Null(resolved.Bytes);
        Assert.Equal(".jpg", resolved.Extension);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            /* best effort */
        }
    }
}
