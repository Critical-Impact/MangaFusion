/** Mirrors MangaFusion.Application.Library.ChapterNumber.Normalize (backend) closely enough to
 *  preview commit order in the admin import/migrate wizards — not the source of truth. The backend
 *  re-validates for real (including duplicate-number collisions against a merge target's existing
 *  chapters) at commit time. */
export interface ChapterNumberKey {
  sort: number | null
  key: string
}

function parseNumeric(trimmed: string): number | null {
  if (!/^[+-]?\d+(\.\d+)?$/.test(trimmed)) return null
  const value = Number.parseFloat(trimmed)
  return Number.isNaN(value) ? null : value
}

function trimTrailingZeros(value: number): string {
  return value.toFixed(4).replace(/0+$/, '').replace(/\.$/, '')
}

export function normalizeChapterNumber(
  number: string | null | undefined,
  volume: string | null | undefined = null,
  title: string | null | undefined = null,
): ChapterNumberKey {
  const num = number?.trim()
  if (num) {
    const parsed = parseNumeric(num)
    return parsed !== null ? { sort: parsed, key: trimTrailingZeros(parsed) } : { sort: null, key: num.toLowerCase() }
  }

  const vol = volume?.trim()
  if (vol) {
    const parsed = parseNumeric(vol)
    return parsed !== null
      ? { sort: parsed, key: `vol-${trimTrailingZeros(parsed)}` }
      : { sort: null, key: `vol-${vol.toLowerCase()}` }
  }

  const t = title?.trim()
  if (t) return { sort: null, key: `title-${t.toLowerCase()}` }

  return { sort: null, key: 'oneshot' }
}

/** Ranks items 1..n the same way the library orders chapters — OrderBy(NumberSort ?? +Infinity)
 *  .ThenBy(NumberKey) — and returns each item's 1-based projected position. Items not passed in
 *  (e.g. excluded/non-importing ones) simply have no entry in the returned map. */
export function rankByChapterNumber<T>(
  items: T[],
  get: (item: T) => { number: string | null; volume?: string | null; title?: string | null },
): Map<T, number> {
  const keyed = items.map((item) => {
    const f = get(item)
    return { item, ...normalizeChapterNumber(f.number, f.volume, f.title) }
  })
  keyed.sort((a, b) => {
    const as = a.sort ?? Number.POSITIVE_INFINITY
    const bs = b.sort ?? Number.POSITIVE_INFINITY
    if (as !== bs) return as - bs
    return a.key < b.key ? -1 : a.key > b.key ? 1 : 0
  })

  const order = new Map<T, number>()
  keyed.forEach((k, i) => order.set(k.item, i + 1))
  return order
}
