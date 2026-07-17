using MangaFusion.Domain.Library;
using Microsoft.AspNetCore.Identity;

namespace MangaFusion.Infrastructure.Identity;

/// <summary>
/// Application user. Extends the Identity user with a GUID key. Per-user preferences,
/// follows, and reading progress are modeled as separate domain entities that reference
/// this user's <see cref="IdentityUser{TKey}.Id"/> — except scalar 1:1 attributes of the
/// user themselves (like <see cref="Theme"/>), which live directly on this row.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>The selected UI theme id (e.g. "violet", "seal-ink"); null means "use the default".
    /// Not validated at this layer — the API endpoint that sets it checks against the known id list.</summary>
    public string? Theme { get; set; }

    /// <summary>The user's default reading language code; defaults to "en" for new users, null means
    /// "none set" (a user can clear it in their profile). Used to pre-fill the one-click auto-download
    /// action on a series page. Unlike <see cref="Theme"/>, there's no fixed list to validate against —
    /// language codes are accepted as free strings throughout the app (see
    /// <c>Follow.Languages</c>/<c>Series.Languages</c>).</summary>
    public string? DefaultLanguage { get; set; } = "en";

    /// <summary>The half of the app the user was last in (manga or comics); null means "use the default".
    /// Purely a UI preference — it decides which library the SPA opens on, not what the user may access.</summary>
    public MediaKind? PreferredKind { get; set; }

    /// <summary>When true, the Home dashboard's rails (continue reading, recently downloaded, recently
    /// updated) span both libraries instead of being scoped to the one the user is currently in. Off by
    /// default, so Home matches the rest of the app — every other page shows one library at a time.</summary>
    public bool HomeAcrossLibraries { get; set; }

    /// <summary>The user's Home dashboard layout as a JSON array of ordered items, each
    /// <c>{ type: "rail" | "collection", key: string, visible: bool }</c> — built-in rail ids
    /// ("continue-reading", "recent-downloads", "recently-updated") or a collection GUID. Null means
    /// "use the default" (built-in rails visible in their canonical order, no collections). Stored
    /// opaque here; the SPA owns the shape and the API only round-trips it.</summary>
    public string? DashboardLayout { get; set; }
}
