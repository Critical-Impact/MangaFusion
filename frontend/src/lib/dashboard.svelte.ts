// Per-user Home dashboard layout — which rails/collections show, and in what order. Runes module
// state, the same singleton pattern as mode.svelte.ts / theme.svelte.ts. The DB (ApplicationUser
// .DashboardLayout, via /api/me and PUT /api/me/dashboard) is the source of truth; localStorage is a
// last-known cache so a reload doesn't flash the default layout before /api/me resolves.
//
// The layout is a flat ordered list of items keyed by a built-in rail id or a collection GUID. It is
// stored once (not per kind), but collections are kind-scoped: the edit/render helpers below only ever
// surface the current kind's collections, and any foreign-kind collection entries are preserved
// untouched on save.

import { setDashboardLayout, type Collection, type DashboardItem } from './api'

export const RAIL_KEYS = ['continue-reading', 'recent-downloads', 'recently-updated'] as const
export type RailKey = (typeof RAIL_KEYS)[number]

export const RAIL_LABELS: Record<RailKey, string> = {
  'continue-reading': 'Continue reading',
  'recent-downloads': 'Recently downloaded',
  'recently-updated': 'Recently updated',
}

/** A dashboard item resolved for display/editing: the stored shape plus a human label. */
export interface ResolvedDashboardItem {
  type: 'rail' | 'collection'
  key: string
  visible: boolean
  label: string
}

const STORAGE_KEY = 'mf-dashboard'

function cachedLayout(): DashboardItem[] | null {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    return raw ? (JSON.parse(raw) as DashboardItem[]) : null
  } catch {
    return null
  }
}

export const dashboardState = $state<{ layout: DashboardItem[] | null }>({ layout: cachedLayout() })

function cache() {
  try {
    if (dashboardState.layout) localStorage.setItem(STORAGE_KEY, JSON.stringify(dashboardState.layout))
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* ignore — cache is best-effort */
  }
}

/** Called once the session loads: the account's saved layout wins over the local cache. */
export function syncFromSession(layout: DashboardItem[] | null | undefined): void {
  dashboardState.layout = layout ?? null
  cache()
}

/** Resolve the ordered items for the current kind, merging the saved layout with the built-in rails
 * and the given collections. Rails not yet placed default to visible; collections not yet placed
 * default to hidden. Layout entries that aren't a built-in rail or one of these collections (i.e.
 * foreign-kind or deleted collections) are omitted here — but preserved by {@link setLayoutForKind}. */
export function resolveItems(collections: Collection[]): ResolvedDashboardItem[] {
  const colById = new Map(collections.map((c) => [c.id, c]))
  const available = new Set<string>([...RAIL_KEYS, ...collections.map((c) => c.id)])

  const result: ResolvedDashboardItem[] = []
  const placed = new Set<string>()

  for (const it of dashboardState.layout ?? []) {
    if (!available.has(it.key) || placed.has(it.key)) continue
    placed.add(it.key)
    result.push({
      type: it.type,
      key: it.key,
      visible: it.visible,
      label: it.type === 'rail' ? RAIL_LABELS[it.key as RailKey] : (colById.get(it.key)?.name ?? ''),
    })
  }

  for (const k of RAIL_KEYS) {
    if (placed.has(k)) continue
    placed.add(k)
    result.push({ type: 'rail', key: k, visible: true, label: RAIL_LABELS[k] })
  }
  for (const c of collections) {
    if (placed.has(c.id)) continue
    placed.add(c.id)
    result.push({ type: 'collection', key: c.id, visible: false, label: c.name })
  }

  return result
}

/** Persist an edited item list for the current kind, preserving any foreign-kind collection entries
 * that {@link resolveItems} didn't surface. Optimistic: updates state + cache immediately, PUTs in the
 * background. */
export function setLayoutForKind(items: ResolvedDashboardItem[], collections: Collection[]): void {
  const knownKeys = new Set<string>([...RAIL_KEYS, ...collections.map((c) => c.id)])
  const preserved = (dashboardState.layout ?? []).filter(
    (it) => it.type === 'collection' && !knownKeys.has(it.key),
  )
  const merged: DashboardItem[] = [
    ...items.map((it) => ({ type: it.type, key: it.key, visible: it.visible })),
    ...preserved,
  ]

  dashboardState.layout = merged
  cache()
  setDashboardLayout(merged).catch(() => {
    /* best-effort — re-persists on the next change */
  })
}
