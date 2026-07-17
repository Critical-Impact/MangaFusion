using MangaFusion.Contracts.Models;
using MangaFusion.Domain.Library;
using MangaFusion.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MangaFusion.Infrastructure.Library;

/// <summary>Find-or-create resolution of domain <see cref="Tag"/> entities, shared by the source
/// import path (matched by source + source tag id) and local import (matched by name only, since
/// manually-created series have no source tag registry).
///
/// Every lookup is scoped to a <see cref="MediaKind"/>: tag rows are per-library, so a comic "Horror"
/// (a ComicVine concept) is a different row from a manga "Horror" (a MangaDex genre), and neither can
/// leak into the other's browse facets.</summary>
public sealed class TagResolver(AppDbContext db)
{
    public async Task<List<Tag>> ResolveSourceTagsAsync(
        MediaKind kind, string sourceId, IReadOnlyList<SourceTagRef> refs, CancellationToken ct)
    {
        if (refs.Count == 0)
        {
            return [];
        }

        var ids = refs.Select(r => r.Id).ToList();
        var existing = await db.Tags
            .Where(t => t.Kind == kind && t.SourceId == sourceId &&
                        t.SourceTagId != null && ids.Contains(t.SourceTagId!))
            .ToListAsync(ct);

        var result = new List<Tag>();
        foreach (var r in refs)
        {
            var tag = existing.FirstOrDefault(t => t.SourceTagId == r.Id);
            if (tag is null)
            {
                tag = new Tag
                {
                    Kind = kind, Name = r.Name, Group = r.Group, SourceId = sourceId, SourceTagId = r.Id,
                };
                db.Tags.Add(tag);
                existing.Add(tag);
            }
            else
            {
                // Names/groups can drift between fetches (a source renaming or re-grouping a tag).
                tag.Name = r.Name;
                tag.Group = r.Group;
            }

            result.Add(tag);
        }

        return result;
    }

    public async Task<List<Tag>> ResolveOrCreateByNameAsync(
        MediaKind kind, IReadOnlyList<string> names, CancellationToken ct)
    {
        var wanted = names.Select(n => n.Trim()).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var wantedLower = wanted.Select(n => n.ToLowerInvariant()).ToList();
        var existing = await db.Tags
            .Where(t => t.Kind == kind && wantedLower.Contains(t.Name.ToLower()))
            .ToListAsync(ct);

        var result = new List<Tag>();
        foreach (var name in wanted)
        {
            var tag = existing.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (tag is null)
            {
                // No group hint for free-typed local tags — "other" until re-tagged against a known one.
                tag = new Tag { Kind = kind, Name = name, Group = "other" };
                db.Tags.Add(tag);
                existing.Add(tag);
            }

            result.Add(tag);
        }

        return result;
    }
}
