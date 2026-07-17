namespace MangaFusion.Infrastructure.Library;

/// <summary>Fuzzy title-similarity scoring shared by <see cref="MigrationMatcher"/> (CBZ migration,
/// matched against MangaDex) and <c>ImportMatcher</c> (the MangaUpdates-assisted import wizard). Pure
/// string comparison — no I/O, no source-specific knowledge.</summary>
public static class TitleMatching
{
    /// <summary>Best similarity of <paramref name="needle"/> against any of <paramref name="candidates"/>
    /// (typically a title + its alt-titles): 1.0 for an exact normalized match, 0.75 for a substring
    /// containment either direction, otherwise the best Jaccard token overlap. 0 if nothing compares.</summary>
    public static double Score(string needle, IEnumerable<string> candidates)
    {
        var n = Normalize(needle);
        if (n.Length == 0)
        {
            return 0;
        }

        var best = 0.0;
        foreach (var raw in candidates)
        {
            var c = Normalize(raw);
            if (c.Length == 0)
            {
                continue;
            }

            if (c == n)
            {
                return 1.0; // exact match on any alt-title short-circuits — can't do better
            }

            if (c.Contains(n) || n.Contains(c))
            {
                best = Math.Max(best, 0.75);
                continue;
            }

            best = Math.Max(best, TokenOverlap(n, c));
        }

        return best;
    }

    public static string Normalize(string s) =>
        new string(s.ToLowerInvariant().Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray())
            .Trim();

    public static double TokenOverlap(string a, string b)
    {
        var ta = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var tb = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (ta.Count == 0 || tb.Count == 0)
        {
            return 0;
        }

        var union = ta.Union(tb).Count();
        return union == 0 ? 0 : (double)ta.Intersect(tb).Count() / union;
    }
}
