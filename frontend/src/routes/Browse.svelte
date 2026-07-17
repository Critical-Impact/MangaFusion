<script lang="ts">
  import { onMount } from 'svelte'
  import { push } from 'svelte-spa-router'
  import { searchSeries, getTags, getSources, type Series, type SourceSummary } from '../lib/api'
  import { PagedSeriesSearch } from '../lib/pagedSeriesSearch.svelte'
  import { currentKind } from '../lib/mode.svelte'
  import { canBrowse } from '../lib/terms.svelte'
  import SeriesGrid from '../lib/SeriesGrid.svelte'
  import FilterBar from '../lib/FilterBar.svelte'
  import MultiSelectDropdown from '../lib/MultiSelectDropdown.svelte'
  import Pager from '../lib/Pager.svelte'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // The nav hides Browse in a library with no downloadable catalogue (comics), but the route is still
  // reachable by URL — send it home rather than rendering a search that can't lead anywhere.
  onMount(() => {
    if (!canBrowse()) push('/')
  })

  // Browse is the download catalogue, so the sources are the ones that can actually serve pages
  // (MangaDex + the native web-scraping sources) — metadata-only sources like MangaUpdates are excluded.
  // Resolved once, lazily; the search/tag callbacks below await it, which keeps PagedSeriesSearch's eager
  // first load from racing it. The user then picks among them (defaulting to the first).
  // Reserved id for the aggregate "search everything" option (mirrors AggregateCatalogSearch.SourceId).
  const ALL_ID = 'all'

  let sources = $state<SourceSummary[]>([])
  const sourcesPromise = getSources(currentKind()).then((all) => {
    sources = all.filter((s) => s.capabilities.includes('Download'))
    return sources
  })

  const resolve = (list: SourceSummary[], requested: string) =>
    list.find((s) => s.id === requested) ?? list[0]

  // "All sources" prepended to the picker (it isn't a real registered source). Results still carry
  // their real sourceId, so navigation/add-to-library work unchanged; a per-card badge shows the origin.
  const pickerSources = $derived([
    { id: ALL_ID, displayName: 'All sources', capabilities: [], requiresAuth: false, configured: false },
    ...sources,
  ])
  const sourceLabels = $derived(Object.fromEntries(sources.map((s) => [s.id, s.displayName])))

  const orders = [
    { v: 'latest', l: 'Recently updated' },
    { v: 'newest', l: 'Newest' },
    { v: 'relevance', l: 'Relevance' },
    { v: 'rating', l: 'Rating' },
    { v: 'followers', l: 'Followers' },
    { v: 'title', l: 'Title (A–Z)' },
    { v: 'year', l: 'Year' },
  ]

  const ratings = [
    { v: '', l: 'Any rating' },
    { v: 'Safe', l: 'Safe' },
    { v: 'Suggestive', l: 'Suggestive' },
    { v: 'Erotica', l: 'Erotica' },
    { v: 'Pornographic', l: 'Pornographic' },
  ]

  const search = new PagedSeriesSearch<Series>({
    pageSize: 24,
    defaultSort: 'latest',
    maxResults: 10000, // MangaDex rejects offset + limit > 10000.
    sortParam: 'order',
    // Browse only ever runs against a manga catalogue (see canBrowse), so its facets stay genre/theme.
    facetGroups: ['genre', 'theme'],
    search: async (p) => {
      const list = await sourcesPromise
      if (list.length === 0) throw new Error('No downloadable source is available for this library.')
      // "All": fan out over every source for this library (facets/rating don't apply across sources).
      if (p.source === ALL_ID) {
        return searchSeries(ALL_ID, p.q, {
          order: p.sort,
          limit: p.limit,
          offset: p.offset,
          kind: currentKind(),
        })
      }
      const source = resolve(list, p.source)
      return searchSeries(source.id, p.q, {
        tag: [...(p.facets.genre ?? []), ...(p.facets.theme ?? [])],
        rating: p.rating || undefined,
        order: p.sort,
        limit: p.limit,
        offset: p.offset,
      })
    },
  })

  // Once the source list is known, reflect a valid default into the picker (unless the URL already
  // selected a valid one). The first results load already resolves ''→first, so this only fixes the
  // picker's displayed value; it doesn't need to re-fetch.
  sourcesPromise.then((list) => {
    if (list.length > 0 && search.source !== ALL_ID && !list.some((s) => s.id === search.source))
      search.source = list[0].id
  })

  // Tag facets are per-source: MangaDex publishes genres/themes; the web-scraping sources expose none,
  // so their filter dropdowns hide. Reloads whenever the active source changes.
  let hasFacets = $state(false)
  $effect(() => {
    const sourceId = search.source
    if (sourceId) loadTagsFor(sourceId)
  })
  async function loadTagsFor(sourceId: string) {
    try {
      const tags = await getTags(sourceId)
      const group = (g: string) => tags.filter((t) => t.group === g).map((t) => ({ id: t.id, name: t.name }))
      search.options.genre = group('genre')
      search.options.theme = group('theme')
      hasFacets = search.options.genre.length + search.options.theme.length > 0
    } catch {
      search.options.genre = []
      search.options.theme = []
      hasFacets = false
    }
  }

  // Switching source invalidates the previous source's tag ids and rating — clear them, then reload.
  function onSourceChange() {
    search.selected.genre = []
    search.selected.theme = []
    search.rating = ''
    search.reload()
  }

  const isAll = $derived(search.source === ALL_ID)
  const sourceName = $derived(
    isAll ? 'All sources' : (sources.find((s) => s.id === search.source)?.displayName ?? ''),
  )
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-5 text-[1.4rem]">Browse</h1>

  <FilterBar
    bind:query={search.q}
    placeholder={sourceName ? `Search ${sourceName}…` : 'Search…'}
    onsubmit={() => search.reload()}
    sort={orders}
    bind:order={search.sort}
    onsort={() => search.reload()}
  >
    {#snippet filters()}
      {#if sources.length > 0}
        <Select type="single" bind:value={search.source} onValueChange={onSourceChange}>
          <SelectTrigger>{sourceName || 'Source'}</SelectTrigger>
          <SelectContent>
            {#each pickerSources as s (s.id)}<SelectItem value={s.id} label={s.displayName}>{s.displayName}</SelectItem>{/each}
          </SelectContent>
        </Select>
      {/if}
      {#if hasFacets}
        <MultiSelectDropdown label="Genre" options={search.options.genre ?? []} bind:selected={search.selected.genre} onchange={() => search.reload()} />
        <MultiSelectDropdown label="Theme" options={search.options.theme ?? []} bind:selected={search.selected.theme} onchange={() => search.reload()} />
        <Select type="single" bind:value={search.rating} onValueChange={() => search.reload()}>
          <SelectTrigger>{ratings.find((r) => r.v === search.rating)?.l ?? search.rating}</SelectTrigger>
          <SelectContent>
            {#each ratings as r (r.v)}<SelectItem value={r.v} label={r.l}>{r.l}</SelectItem>{/each}
          </SelectContent>
        </Select>
      {/if}
    {/snippet}
  </FilterBar>

  {#if search.loading}
    <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
  {:else if search.error}
    <Alert variant="destructive"><AlertDescription>{search.error}</AlertDescription></Alert>
  {:else if search.items.length === 0}
    <p class="muted">No results.</p>
  {:else}
    <SeriesGrid series={search.items} sourceLabels={isAll ? sourceLabels : undefined} />
    <Pager page={search.page} totalPages={search.totalPages} onprev={() => search.go(-1)} onnext={() => search.go(1)}>
      {#snippet label()}Page {search.page + 1} of {search.totalPages}{search.exceedsMaxResults ? '+' : ''}{/snippet}
    </Pager>
  {/if}
</section>
