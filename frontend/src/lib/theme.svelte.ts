// Reactive theme selection, shared across the app (Svelte 5 runes module state — same
// singleton pattern as session.svelte.ts / signalr.svelte.ts).
//
// The database (ApplicationUser.Theme, via /api/me and PUT /api/me/theme) is the source
// of truth — it's what makes the choice follow the user across devices/sessions. A small
// localStorage value is still kept, but purely as a last-known-theme paint cache: the
// DOM's `data-theme` attribute is set synchronously by an inline script in index.html,
// before this module (or any JS, or the /api/me round-trip) runs, so there's no flash of
// the wrong theme on load. Once the session loads, syncFromSession() lets the DB value
// (if any) win and overwrites both the DOM attribute and the cache.

import { setUserTheme } from './api'

export const THEMES = [
  { id: 'violet', label: 'Violet' },
  { id: 'seal-ink', label: 'Seal Ink' },
  { id: 'jade', label: 'Jade Screentone' },
  { id: 'momiji', label: 'Momiji' },
] as const

export type ThemeId = (typeof THEMES)[number]['id']

const STORAGE_KEY = 'mf-theme'
const DEFAULT_THEME: ThemeId = 'violet'

function isThemeId(value: string | null | undefined): value is ThemeId {
  return THEMES.some((t) => t.id === value)
}

export const themeState = $state<{ id: ThemeId }>({ id: DEFAULT_THEME })

function apply(id: ThemeId) {
  themeState.id = id
  document.documentElement.dataset.theme = id
  localStorage.setItem(STORAGE_KEY, id)
}

/** Called from the picker: applies immediately (optimistic), persists to the DB in the background. */
export function setTheme(id: ThemeId): void {
  apply(id)
  setUserTheme(id).catch(() => {
    /* best-effort — the choice still applied locally and will retry next time it's changed */
  })
}

/** Called once the session loads: the account's saved theme (if any) wins over the local cache. */
export function syncFromSession(theme: string | null | undefined): void {
  if (isThemeId(theme)) {
    apply(theme)
    return
  }
  const cached = localStorage.getItem(STORAGE_KEY)
  apply(isThemeId(cached) ? cached : DEFAULT_THEME)
}
