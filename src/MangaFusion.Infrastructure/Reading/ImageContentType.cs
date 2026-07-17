namespace MangaFusion.Infrastructure.Reading;

/// <summary>Maps image file names to content types and decides which archive/folder entries are pages.</summary>
internal static class ImageContentType
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".avif", ".bmp",
    };

    public static bool IsImage(string name) => Extensions.Contains(Path.GetExtension(name));

    public static string ForName(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".avif" => "image/avif",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream",
    };
}
