using MangaFusion.Application.Library;
using MangaFusion.Domain.Library;

namespace MangaFusion.Infrastructure.Library;

/// <summary>The pure chapter-key dedup check shared by <see cref="ChapterFileImporter"/> (image pages)
/// and <see cref="ProseChapterImporter"/> (prose). Extracted so the two importers agree on how a
/// chapter's identity is computed and rejected as a duplicate, without the prose path threading an
/// <c>if</c> through the dense, page-shaped image importer.</summary>
internal static class ChapterKeys
{
    /// <summary>Qualifies each spec to its chapter key and throws if any collides — with another spec in
    /// the same request, or with an existing chapter of <paramref name="series"/> in the same language.
    /// The caller must have loaded <c>series.Chapters</c>.</summary>
    public static List<(LocalChapterSpec Spec, string Key)> KeyAndEnsureUnique(
        Series series, string language, IReadOnlyList<LocalChapterSpec> specs)
    {
        var keyed = specs
            .Select(s => (
                Spec: s,
                Key: ChapterNumber.QualifyKey(
                    series.SortMode, ChapterNumber.Normalize(s.Number, s.Volume).Key, s.Volume)))
            .ToList();

        var existing = series.Chapters
            .Where(c => c.Language == language)
            .Select(c => c.NumberKey)
            .ToHashSet();

        var seen = new HashSet<string>();
        foreach (var (_, key) in keyed)
        {
            if (!seen.Add(key) || existing.Contains(key))
            {
                throw new InvalidOperationException($"Chapter '{key}' already exists in {language}.");
            }
        }

        return keyed;
    }
}
