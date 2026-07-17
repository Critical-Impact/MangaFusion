using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Find-or-create resolution of domain <see cref="Author"/> entities, shared by the MangaDex
/// import path (matched by source + source author id) and local import (matched by name only, since
/// manually-created series have no source author registry). Unlike <see cref="TagResolver"/>, there is
/// no bulk registry sync — MangaDex has no equivalent of <c>/manga/tag</c> for authors, and there are
/// far too many to pull wholesale, so the local catalog only grows as a byproduct of series add/rescan.</summary>
public sealed class AuthorResolver(AppDbContext db)
{
    public async Task<List<Author>> ResolveSourceAuthorsAsync(
        string sourceId, IReadOnlyList<SourceAuthorRef> refs, CancellationToken ct)
    {
        if (refs.Count == 0)
        {
            return [];
        }

        var ids = refs.Select(r => r.Id).ToList();
        var existing = await db.Authors
            .Where(a => a.SourceId == sourceId && a.SourceAuthorId != null && ids.Contains(a.SourceAuthorId!))
            .ToListAsync(ct);

        var result = new List<Author>();
        foreach (var r in refs)
        {
            var author = existing.FirstOrDefault(a => a.SourceAuthorId == r.Id);
            if (author is null)
            {
                author = new Author { Name = r.Name, SourceId = sourceId, SourceAuthorId = r.Id };
                db.Authors.Add(author);
                existing.Add(author);
            }
            else
            {
                // Names can drift between fetches (a source correcting/renaming an author).
                author.Name = r.Name;
            }

            result.Add(author);
        }

        return result;
    }

    public async Task<List<Author>> ResolveOrCreateByNameAsync(
        IReadOnlyList<string> names, CancellationToken ct)
    {
        var wanted = names.Select(n => n.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var wantedLower = wanted.Select(n => n.ToLowerInvariant()).ToList();
        var existing = await db.Authors
            .Where(a => a.SourceId == null && wantedLower.Contains(a.Name.ToLower()))
            .ToListAsync(ct);

        var result = new List<Author>();
        foreach (var name in wanted)
        {
            var author = existing.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (author is null)
            {
                author = new Author { Name = name };
                db.Authors.Add(author);
                existing.Add(author);
            }

            result.Add(author);
        }

        return result;
    }
}
