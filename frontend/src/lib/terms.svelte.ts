// Per-kind vocabulary. A comic's "issue" is a manga's "chapter" — the same Chapter row underneath, but
// showing "Chapter 1" on a Batman volume reads as a bug to anyone who reads comics.
//
// Reactive by construction: these are functions over `modeState`, so any component that calls them in
// markup re-renders when the mode switches.

import { modeState, type MediaKind } from './mode.svelte'

const VOCAB = {
  manga: {
    chapter: 'chapter',
    Chapter: 'Chapter',
    chapters: 'chapters',
    Chapters: 'Chapters',
    library: 'Manga',
  },
  comic: {
    chapter: 'issue',
    Chapter: 'Issue',
    chapters: 'issues',
    Chapters: 'Issues',
    library: 'Comics',
  },
  lightnovel: {
    chapter: 'chapter',
    Chapter: 'Chapter',
    chapters: 'chapters',
    Chapters: 'Chapters',
    library: 'Light Novels',
  },
} as const

type Term = keyof (typeof VOCAB)['manga']

export function t(term: Term): string {
  return VOCAB[modeState.kind][term]
}

/** Whether the current library has a catalogue worth browsing.
 *
 * Browse exists to find something and download it, so only a kind with a downloadable source gets one.
 * An explicit allowlist, not an exclusion list, so a newly-added kind is browse-less by default rather
 * than silently inheriting manga's Browse page:
 * - Manga has MangaDex (an IDownloadSource) — the one browsable kind today.
 * - Comics: ComicVine is metadata-only; comics enter via the local-import wizard's own matching.
 * - Light novels: MangaUpdates is metadata-only and no download source exists in v1; they enter via
 *   local prose import. A Browse page would be a search that ends in a dead end. */
export function canBrowseKind(kind: MediaKind): boolean {
  return kind === 'manga'
}

export function canBrowse(): boolean {
  return canBrowseKind(modeState.kind)
}

/** The tag groups that make up the browse/library filter facets for the current library.
 *
 * Manga sources publish a genre/theme vocabulary. ComicVine has none — it credits a publisher, characters
 * and concepts instead (see ComicVineMapper), so those are what a comic filters on. */
export function facetGroups(): { group: string; label: string }[] {
  return modeState.kind === 'comic'
    ? [
        { group: 'publisher', label: 'Publisher' },
        { group: 'character', label: 'Character' },
        { group: 'concept', label: 'Concept' },
      ]
    : [
        { group: 'genre', label: 'Genre' },
        { group: 'theme', label: 'Theme' },
      ]
}
