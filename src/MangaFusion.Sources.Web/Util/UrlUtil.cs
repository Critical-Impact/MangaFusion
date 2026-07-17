using System.Buffers.Text;
using System.Text;

namespace MangaFusion.Sources.Web.Util;

/// <summary>URL/identity helpers shared across web sources.</summary>
public static class UrlUtil
{
    /// <summary>Strips scheme + host from an absolute URL, keeping path (+ query + fragment). Mirrors
    /// Tachiyomi's <c>setUrlWithoutDomain</c> so a stored series/chapter URL survives a domain change.</summary>
    public static string RemoveDomain(string url)
    {
        if (string.IsNullOrEmpty(url)) return url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath;
            if (!string.IsNullOrEmpty(uri.Query)) path += uri.Query;
            if (!string.IsNullOrEmpty(uri.Fragment)) path += uri.Fragment;
            return path;
        }
        return url;
    }

    /// <summary>Resolves a possibly-relative site path against <paramref name="baseUrl"/> into an
    /// absolute URL. Handles absolute URLs, protocol-relative <c>//host/…</c>, and root/relative paths.</summary>
    public static string? Absolute(string baseUrl, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Trim();
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;
        if (path.StartsWith("//")) return "https:" + path;
        var b = baseUrl.TrimEnd('/');
        return path.StartsWith('/') ? b + path : b + "/" + path;
    }

    /// <summary>Encodes a site path into an opaque, URL/route-safe id (Base64Url). Web-source series and
    /// chapter ids are paths that contain slashes, which can't ride in a path route segment; encoding
    /// them keeps the existing <c>{sourceId}/{seriesId}</c> endpoints working unchanged.</summary>
    public static string EncodeId(string path) =>
        Base64Url.EncodeToString(Encoding.UTF8.GetBytes(path));

    /// <summary>Reverses <see cref="EncodeId"/> back to the original site path.</summary>
    public static string DecodeId(string id) =>
        Encoding.UTF8.GetString(Base64Url.DecodeFromChars(id));
}
