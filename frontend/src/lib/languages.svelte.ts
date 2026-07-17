// Known-language list backing every language picker (default language, follow/series
// auto-download languages) — fetched once from the backend's fixed known-language table
// (GET /api/languages, backed by MangaLanguage.KnownLanguages) and cached for the session, so
// pickers can offer a language before any release has ever appeared in it.

import { getLanguages, type LanguageOption } from './api'

export const languagesState = $state<{ items: LanguageOption[] }>({ items: [] })

let loaded: Promise<void> | null = null

export function ensureLanguagesLoaded(): Promise<void> {
  if (!loaded) {
    loaded = getLanguages()
      .then((items) => {
        languagesState.items = items
      })
      .catch(() => {
        loaded = null
      })
  }
  return loaded
}

export function languageName(code: string): string {
  return languagesState.items.find((l) => l.code === code)?.name ?? code
}
