// Which half of the app you're in — manga (MangaFusion) or comics (ComicFusion). Svelte 5 runes module
// state, the same singleton pattern as theme.svelte.ts / session.svelte.ts.
//
// The database (ApplicationUser.PreferredKind, via /api/me and PUT /api/me/mode) is the source of truth,
// so the choice follows the user across devices. localStorage is only a last-known-mode cache so a reload
// doesn't briefly flash the wrong library before /api/me resolves.
//
// Unlike theme.svelte.ts, this module does NOT import from ./api — api.ts imports `currentKind` from here
// to scope every library request, and going both ways would be a circular import. The one PUT it needs is
// small enough to inline.

export const MODES = [
  { kind: 'manga', brand: 'MangaFusion', label: 'Manga' },
  { kind: 'comic', brand: 'ComicFusion', label: 'Comics' },
  { kind: 'lightnovel', brand: 'LightNovelFusion', label: 'Light Novels' },
] as const

export type MediaKind = (typeof MODES)[number]['kind']

const STORAGE_KEY = 'mf-mode'
const DEFAULT_KIND: MediaKind = 'manga'

function isKind(value: string | null | undefined): value is MediaKind {
  return MODES.some((m) => m.kind === value)
}

function cachedKind(): MediaKind {
  const cached = localStorage.getItem(STORAGE_KEY)
  return isKind(cached) ? cached : DEFAULT_KIND
}

// Seeded from the cache at module load, not left at the default until /api/me resolves — the login
// screen renders the brand before there's a session to read a preference from, and a returning
// ComicFusion user shouldn't be greeted by the MangaFusion login page.
export const modeState = $state<{ kind: MediaKind }>({ kind: cachedKind() })

/** Whether the Home rails span both libraries instead of just the one you're in
 * (ApplicationUser.HomeAcrossLibraries). Off by default, so Home matches every other page. Lives here
 * rather than in a settings module because it answers the same question this module owns — which
 * library (or libraries) am I looking at — and api.ts already reads that answer from here. */
export const homeScope = $state<{ acrossLibraries: boolean }>({ acrossLibraries: false })

/** Non-reactive read, for api.ts — it scopes requests, it doesn't render. */
export function currentKind(): MediaKind {
  return modeState.kind
}

/** The kind to scope a Home rail to, or null for "both libraries". */
export function homeKind(): MediaKind | null {
  return homeScope.acrossLibraries ? null : modeState.kind
}

export function setHomeAcrossLibraries(acrossLibraries: boolean): void {
  homeScope.acrossLibraries = acrossLibraries
  fetch('/api/me/home-scope', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ acrossLibraries }),
  }).catch(() => {
    /* best-effort, same as setMode */
  })
}

export function isComic(): boolean {
  return modeState.kind === 'comic'
}

export function brandName(kind: MediaKind = modeState.kind): string {
  return MODES.find((m) => m.kind === kind)!.brand
}

function apply(kind: MediaKind) {
  modeState.kind = kind
  localStorage.setItem(STORAGE_KEY, kind)
  document.title = brandName(kind)
}

/** Applies immediately (optimistic), persists to the DB in the background. */
export function setMode(kind: MediaKind): void {
  if (kind === modeState.kind) return
  apply(kind)
  fetch('/api/me/mode', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    credentials: 'include',
    body: JSON.stringify({ mode: kind }),
  }).catch(() => {
    /* best-effort — the switch still applied locally and re-persists next time it changes */
  })
}

/** Called once the session loads: the account's saved preferences win over the local cache. */
export function syncFromSession(
  preferredKind: string | null | undefined,
  acrossLibraries: boolean | null | undefined,
): void {
  homeScope.acrossLibraries = acrossLibraries ?? false
  apply(isKind(preferredKind) ? preferredKind : cachedKind())
}
