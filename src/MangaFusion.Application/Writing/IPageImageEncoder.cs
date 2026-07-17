namespace MangaFusion.Application.Writing;

/// <summary>Re-encoded page bytes plus the file extension they should be stored under.</summary>
public sealed record EncodedPage(byte[] Bytes, string Extension);

/// <summary>Optionally re-encodes a single page image to a smaller/alternate format. Implementations
/// must never throw for merely-bad input (corrupt/unsupported/animated source) — return <c>null</c>
/// instead, meaning "leave this page alone." This is the seam a future codec (e.g. JPEG XL, once a
/// mature .NET binding exists) plugs into without touching any caller.</summary>
public interface IPageImageEncoder
{
    Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct);
}
