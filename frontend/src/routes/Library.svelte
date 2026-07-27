<script lang="ts">
  import { push } from 'svelte-spa-router'
  import { getLibrary, getLibraryTags, getSources, type LibrarySeries } from '../lib/api'
  import { PagedSeriesSearch } from '../lib/pagedSeriesSearch.svelte'
  import { BestEffortList } from '../lib/bestEffortList.svelte'
  import { isComic } from '../lib/mode.svelte'
  import { t, facetGroups, canBrowse } from '../lib/terms.svelte'
  import PosterCard from '../lib/PosterCard.svelte'
  import FilterBar from '../lib/FilterBar.svelte'
  import MultiSelectDropdown from '../lib/MultiSelectDropdown.svelte'
  import Pager from '../lib/Pager.svelte'
  import { Badge } from '../lib/components/ui/badge/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // Sort field + direction flattened into one FilterBar select, same pattern as ResultsBrowser's orders.
  const sorts = [
    { v: 'title-asc', l: 'Title (A–Z)' },
    { v: 'title-desc', l: 'Title (Z–A)' },
    { v: 'added-desc', l: 'Recently added' },
    { v: 'added-asc', l: 'Oldest added' },
    { v: 'year-desc', l: 'Year (newest)' },
    { v: 'year-asc', l: 'Year (oldest)' },
    { v: 'chapters-desc', l: `Most ${t('chapters')}` },
    { v: 'chapters-asc', l: `Fewest ${t('chapters')}` },
  ]

  // Genre/theme for manga; publisher/character/concept for comics — ComicVine has no genre vocabulary.
  const facets = facetGroups()

  const ratings = [
    { v: '', l: 'Any rating' },
    { v: 'Safe', l: 'Safe' },
    { v: 'Suggestive', l: 'Suggestive' },
    { v: 'Erotica', l: 'Erotica' },
    { v: 'Pornographic', l: 'Pornographic' },
  ]

  // "local" is deliberately not a registered source (see LocalSourceConstants), so it's added here
  // rather than coming back from GET /api/sources.
  const sourceOptions = new BestEffortList(async () => [
    { v: '', l: 'Any source' },
    ...(await getSources()).map((s) => ({ v: s.id, l: s.displayName })),
    { v: 'local', l: 'Local' },
  ])

  const search = new PagedSeriesSearch<LibrarySeries>({
    pageSize: 24,
    defaultSort: 'title-asc',
    facetGroups: facets.map((f) => f.group),
    supportsRating: !isComic(), // ComicVine publishes no rating; the filter is hidden for comics.
    search: async (p) => {
      const [sort, order] = p.sort.split('-') as [string, 'asc' | 'desc']
      return getLibrary({
        q: p.q,
        tagFacets: facets.map((f) => p.facets[f.group] ?? []),
        rating: p.rating || undefined,
        sort,
        order,
        limit: p.limit,
        offset: p.offset,
        sourceId: p.source || undefined,
      })
    },
    loadTags: async () => {
      const loaded = await Promise.all(facets.map((f) => getLibraryTags(f.group)))
      return Object.fromEntries(facets.map((f, i) => [f.group, loaded[i]]))
    },
  })

  const sourceLabel = (id: string) => sourceOptions.items.find((o) => o.v === id)?.l ?? id

  const hasFilters = $derived(!!search.q || search.hasAnyFacetSelected || !!search.rating)
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-5 text-[1.4rem]">Library</h1>

  <FilterBar
    bind:query={search.q}
    placeholder="Search your library…"
    onsubmit={() => search.reload()}
    sort={sorts}
    bind:order={search.sort}
    onsort={() => search.reload()}
  >
    {#snippet filters()}
      {#each facets as f (f.group)}
        <MultiSelectDropdown
          label={f.label}
          options={search.options[f.group] ?? []}
          bind:selected={search.selected[f.group]}
          onchange={() => search.reload()}
        />
      {/each}
      <!-- ComicVine publishes no content rating, so the filter would always be a no-op for comics. -->
      {#if !isComic()}
        <Select type="single" bind:value={search.rating} onValueChange={() => search.reload()}>
          <SelectTrigger>{ratings.find((r) => r.v === search.rating)?.l ?? search.rating}</SelectTrigger>
          <SelectContent>
            {#each ratings as r (r.v)}<SelectItem value={r.v} label={r.l}>{r.l}</SelectItem>{/each}
          </SelectContent>
        </Select>
      {/if}
      <Select type="single" bind:value={search.source} onValueChange={() => search.reload()}>
        <SelectTrigger>{sourceOptions.items.find((s) => s.v === search.source)?.l ?? 'Any source'}</SelectTrigger>
        <SelectContent>
          {#each sourceOptions.items as s (s.v)}<SelectItem value={s.v} label={s.l}>{s.l}</SelectItem>{/each}
        </SelectContent>
      </Select>
    {/snippet}
  </FilterBar>

  {#if search.loading}
    <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
  {:else if search.error}
    <Alert variant="destructive"><AlertDescription>{search.error}</AlertDescription></Alert>
  {:else if search.items.length === 0}
    <p class="muted">
      {#if hasFilters}
        No matches.
      {:else if !canBrowse()}
        <!-- No Browse for this kind (comics, light novels) — the library fills from local imports, so
             point at the importer rather than telling the user to search a catalogue that doesn't exist. -->
        {#if isComic()}
          Your comic library is empty. Import comics from the inbox under Admin → Import.
        {:else}
          Your light novel library is empty. Import novels from the inbox under Admin → Local.
        {/if}
      {:else}
        Your library is empty. Search and open a series to add it.
      {/if}
    </p>
  {:else}
    <div class="poster-grid">
      {#each search.items as s (s.id)}
        <PosterCard title={s.title} coverUrl={s.coverUrl} onclick={() => push(`/library/${s.id}`)}>
          {#snippet overlay()}
            {#if s.followed}
              <Badge variant="ok" class="absolute bottom-[0.4rem] left-[0.4rem]">following</Badge>
            {/if}
            {#if s.sources.length > 0}
              <Badge variant="info" class="absolute bottom-[0.4rem] right-[0.4rem]">
                {sourceLabel(s.sources.find((x) => x !== 'local') ?? s.sources[0])}
              </Badge>
            {/if}
          {/snippet}
        </PosterCard>
      {/each}
    </div>
    <Pager
      page={search.page}
      totalPages={search.totalPages}
      total={search.total}
      pageSize={search.pageSize}
      onprev={() => search.go(-1)}
      onnext={() => search.go(1)}
    />
  {/if}
</section>
