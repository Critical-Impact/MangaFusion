namespace MangaFusion.Application.Sources;

/// <summary>Merges per-source result lists into one, round-robin: the 1st item of every source, then
/// the 2nd of every source, and so on. Keeps each source's own ordering within its slots and stops a
/// single source from dominating the top. Pure and order-preserving for testability.</summary>
public static class SourceResultMerger
{
    public static IReadOnlyList<T> Interleave<T>(IReadOnlyList<IReadOnlyList<T>> lists)
    {
        if (lists.Count == 0) return [];

        var merged = new List<T>(lists.Sum(l => l.Count));
        var depth = lists.Max(l => l.Count);
        for (var i = 0; i < depth; i++)
        {
            foreach (var list in lists)
            {
                if (i < list.Count) merged.Add(list[i]);
            }
        }
        return merged;
    }
}
