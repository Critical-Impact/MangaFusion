// Keeps a page's filter/sort/page state mirrored into the URL querystring via replaceState, so
// browser back/forward doesn't unwind each filter tweak as its own history entry, but landing back
// on the page (e.g. after opening a series) restores the filters that were active when it was left.

export type QueryValue = string | number | undefined | null | string[]

export function buildQueryString(params: Record<string, QueryValue>): string {
  const qs = new URLSearchParams()
  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') continue
    if (Array.isArray(value)) {
      for (const v of value) qs.append(key, v)
    } else {
      qs.set(key, String(value))
    }
  }
  return qs.toString()
}

/** Replaces the current history entry's querystring without adding a new one or notifying the router. */
export function replaceQueryString(qs: string): void {
  const path = location.hash.split('?')[0] || '#/'
  const next = qs ? `${path}?${qs}` : path
  if (next !== location.hash) history.replaceState(history.state, '', next)
}
