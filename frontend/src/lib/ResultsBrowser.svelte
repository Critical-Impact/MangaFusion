<script lang="ts">
  import { onMount } from 'svelte'
  import { searchSeries, type Series } from './api'
  import SeriesGrid from './SeriesGrid.svelte'
  import FilterBar from './FilterBar.svelte'
  import Pager from './Pager.svelte'
  import { Alert, AlertDescription } from './components/ui/alert/index.js'
  import { Spinner } from './components/ui/spinner/index.js'

  let {
    sourceId,
    tag = undefined,
    authorId = undefined,
    placeholder = 'Search…',
  }: { sourceId: string; tag?: string; authorId?: string; placeholder?: string } = $props()

  const pageSize = 24
  // MangaDex rejects offset + limit > 10000.
  const maxResults = 10000

  const orders = [
    { v: 'newest', l: 'Newest' },
    { v: 'latest', l: 'Recently updated' },
    { v: 'relevance', l: 'Relevance' },
    { v: 'rating', l: 'Rating' },
    { v: 'followers', l: 'Followers' },
    { v: 'title', l: 'Title (A–Z)' },
    { v: 'year', l: 'Year' },
  ]

  let q = $state('')
  let order = $state('newest')
  let page = $state(0)
  let total = $state(0)
  let series = $state<Series[]>([])
  let loading = $state(false)
  let error = $state('')

  const totalPages = $derived(Math.max(1, Math.ceil(Math.min(total, maxResults) / pageSize)))

  onMount(load)

  async function load() {
    loading = true
    error = ''
    try {
      const res = await searchSeries(sourceId, q, {
        tag: tag ? [tag] : [],
        authorId,
        order,
        limit: pageSize,
        offset: page * pageSize,
      })
      series = res.items
      total = res.total
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to load.'
      series = []
    } finally {
      loading = false
    }
  }

  function submit() {
    page = 0
    load()
  }

  function onOrder() {
    page = 0
    load()
  }

  function go(delta: number) {
    page = Math.max(0, Math.min(totalPages - 1, page + delta))
    load()
  }
</script>

<FilterBar bind:query={q} {placeholder} onsubmit={submit} sort={orders} bind:order={order} onsort={onOrder} />

{#if error}
  <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
{:else if loading}
  <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
{:else if series.length === 0}
  <p class="muted">Nothing found.</p>
{:else}
  <SeriesGrid {series} />
  <Pager {page} {totalPages} onprev={() => go(-1)} onnext={() => go(1)}>
    {#snippet label()}Page {page + 1} of {totalPages}{total > maxResults ? '+' : ''}{/snippet}
  </Pager>
{/if}
