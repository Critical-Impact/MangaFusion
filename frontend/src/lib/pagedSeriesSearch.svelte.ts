import { onMount } from 'svelte'
import { router } from 'svelte-spa-router'
import { buildQueryString, replaceQueryString } from './urlState'
import type { Paged } from './api'

export interface TagOption {
  id: string
  name: string
}

export interface PagedSeriesSearchOptions<T> {
  pageSize: number
  defaultSort: string
  maxResults?: number
  /** URL query param name for the sort field. Defaults 'sort' (matches Library's existing URLs);
   *  Browse passes 'order' to keep its existing ?order= URLs unchanged. Pure internal refactor —
   *  neither page's URL shape changes. */
  sortParam?: string
  /** The tag groups this page filters on, in display order. Manga filters on genre/theme; comics have no
   *  such vocabulary and filter on publisher/character/concept instead (see terms.svelte's facetGroups).
   *  Each group is its own facet: OR within a group, AND across groups. Each also gets a URL param named
   *  after it, so ?genre=a&genre=b keeps working unchanged. */
  facetGroups?: string[]
  /** Whether this page has a content-rating filter. False for comics — ComicVine publishes no rating, so
   *  the control is hidden; without this the value would still be read from a stale ?rating= in the URL
   *  and silently filter everything out, with no visible control to clear it. */
  supportsRating?: boolean
  search: (params: {
    q: string
    sort: string
    rating: string
    source: string
    /** Selected tag ids per group, keyed by group name. */
    facets: Record<string, string[]>
    limit: number
    offset: number
  }) => Promise<Paged<T>>
  loadTags?: () => Promise<Record<string, TagOption[]>>
}

// See bestEffortList.svelte.ts for why onMount-in-constructor is safe. Same constraint applies:
// nothing above the onMount(...) call in the constructor may be awaited.
export class PagedSeriesSearch<T> {
  q = $state('')
  sort = $state('')
  rating = $state('')
  source = $state('')
  /** Selected tag ids, keyed by group name. */
  selected = $state<Record<string, string[]>>({})
  /** Available tag options, keyed by group name. */
  options = $state<Record<string, TagOption[]>>({})
  page = $state(0)
  total = $state(0)
  items = $state<T[]>([])
  loading = $state(true)
  error = $state('')
  #opts: PagedSeriesSearchOptions<T>

  constructor(opts: PagedSeriesSearchOptions<T>) {
    this.#opts = opts
    this.sort = opts.defaultSort
    for (const g of opts.facetGroups ?? []) {
      this.selected[g] = []
      this.options[g] = []
    }
    onMount(() => this.#init())
  }

  get facetGroups(): string[] {
    return this.#opts.facetGroups ?? []
  }

  get #supportsRating(): boolean {
    return this.#opts.supportsRating ?? true
  }

  get hasAnyFacetSelected(): boolean {
    return this.facetGroups.some((g) => (this.selected[g] ?? []).length > 0)
  }

  // Plain getters, not `$derived` fields: `$derived` in a class-field initializer runs in
  // declaration order *before* the constructor body, which would read `this.#opts` before it's
  // assigned. Getters that read $state fields re-track correctly in template/$effect contexts —
  // memoization (the only thing $derived adds) doesn't matter for this cheap arithmetic.
  get totalPages(): number {
    const capped = this.#opts.maxResults ? Math.min(this.total, this.#opts.maxResults) : this.total
    return Math.max(1, Math.ceil(capped / this.#opts.pageSize))
  }
  get pageSize(): number {
    return this.#opts.pageSize
  }
  get exceedsMaxResults(): boolean {
    return this.#opts.maxResults != null && this.total > this.#opts.maxResults
  }

  async #init() {
    const sortParam = this.#opts.sortParam ?? 'sort'
    const p = new URLSearchParams(router.querystring)
    if (p.get('q')) this.q = p.get('q')!
    if (p.get(sortParam)) this.sort = p.get(sortParam)!
    if (this.#supportsRating && p.get('rating')) this.rating = p.get('rating')!
    if (p.get('source')) this.source = p.get('source')!
    for (const g of this.facetGroups) this.selected[g] = p.getAll(g)
    if (p.get('page')) this.page = Math.max(0, Number(p.get('page')) || 0)

    // Not awaited — matches Browse's/Library's original onMount bodies exactly: results start
    // loading immediately, tag dropdowns populate concurrently and independently. Don't
    // Promise.all this; that would change perceived load latency.
    this.load()
    if (this.#opts.loadTags) {
      try {
        const loaded = await this.#opts.loadTags()
        for (const g of this.facetGroups) this.options[g] = loaded[g] ?? []
      } catch {
        /* filter dropdowns are best-effort */
      }
    }
  }

  #syncUrl() {
    const sortParam = this.#opts.sortParam ?? 'sort'
    const facetParams: Record<string, string[]> = {}
    for (const g of this.facetGroups) facetParams[g] = this.selected[g] ?? []
    replaceQueryString(
      buildQueryString({
        q: this.q,
        [sortParam]: this.sort,
        // Dropped entirely when unsupported, so a stale ?rating= left over from the other library doesn't
        // linger in the URL after a mode switch.
        rating: this.#supportsRating ? this.rating : undefined,
        source: this.source,
        ...facetParams,
        page: this.page || undefined, // deliberately omit page=0 so default-state URLs stay clean
      }),
    )
  }

  // Every filter toggle, sort change and page step fires a fresh load, so several are routinely in flight
  // at once — and on Browse they go out to the source (MangaDex/ComicVine), where latency varies enough
  // that they don't come back in order. Without this, a slow earlier response lands last and overwrites the
  // newer one: the grid shows results for a filter the user has already changed. Only the newest load is
  // allowed to touch the state.
  #seq = 0

  async load() {
    this.#syncUrl()
    const seq = ++this.#seq
    this.loading = true
    this.error = ''
    try {
      const facets: Record<string, string[]> = {}
      for (const g of this.facetGroups) facets[g] = this.selected[g] ?? []
      const res = await this.#opts.search({
        q: this.q,
        sort: this.sort,
        rating: this.rating,
        source: this.source,
        facets,
        limit: this.#opts.pageSize,
        offset: this.page * this.#opts.pageSize,
      })
      if (seq !== this.#seq) return // superseded — a newer load owns the state now
      this.items = res.items
      this.total = res.total
    } catch (err) {
      if (seq !== this.#seq) return // a stale failure must not clobber a newer success
      this.error = err instanceof Error ? err.message : 'Failed to load.'
      this.items = []
    } finally {
      // Only the newest load clears the spinner: an older one finishing first would otherwise report "done"
      // while the request actually being shown is still running.
      if (seq === this.#seq) this.loading = false
    }
  }

  reload() {
    this.page = 0
    this.load()
  }
  go(delta: number) {
    this.page = Math.max(0, Math.min(this.totalPages - 1, this.page + delta))
    this.load()
  }
}
