namespace MangaFusion.Application.Writing;

/// <summary>Re-encoded page bytes plus the file extension they should be stored under.</summary>
public sealed record EncodedPage(byte[] Bytes, string Extension);

/// <summary>Optionally re-encodes a single page image to a smaller/alternate format. Implementations
/// must never throw for merely-bad input (corrupt/unsupported/animated source) — return <c>null</c>
/// instead, meaning "leave this page alone." This is the seam a future codec (e.g. JPEG XL, once a
/// mature .NET binding exists) plugs into without touching any caller.</summary>
public interface IPageImageEncoder
{
    /// <summary>Whether this encoder can produce anything at all (configured on, codec available).
    /// When false, <see cref="TryEncodeAsync"/> always returns <c>null</c>, so callers that would do
    /// expensive prep just to feed it — e.g. cracking open an existing archive to re-encode it in
    /// place — can skip that work entirely rather than doing it for a guaranteed no-op.</summary>
    bool Enabled { get; }

    Task<EncodedPage?> TryEncodeAsync(string sourcePath, CancellationToken ct);
}
