using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Library;

/// <summary>The on-disk folder name for a library. Every path helper that splits per kind goes through
/// this, so the two names are spelled in exactly one place — a typo'd "comic" vs "comics" in one helper
/// would silently point a tool at an empty directory rather than failing.</summary>
public static class MediaKindFolder
{
    public static string For(MediaKind kind) => kind switch
    {
        MediaKind.Comic => "comics",
        _ => "manga",
    };
}
