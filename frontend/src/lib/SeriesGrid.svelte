<script lang="ts">
  import SeriesCard from './SeriesCard.svelte'
  import { getLibraryMembership, type Series } from './api'

  // sourceLabels (id → display name) is passed in the "All sources" view so each card can show which
  // source it came from; undefined for a single-source browse.
  let { series, sourceLabels }: { series: Series[]; sourceLabels?: Record<string, string> } = $props()

  const key = (sourceId: string, sourceSeriesId: string) => `${sourceId}/${sourceSeriesId}`

  // Which of these results are already in the library → their library id, keyed by sourceId/sourceSeriesId.
  // Fetched in one batch whenever the result set changes, so each card can show a ✓ and link straight in
  // instead of re-adding. Best-effort: on failure the cards just fall back to the plain add button.
  let membership = $state<Record<string, string>>({})
  $effect(() => {
    const refs = series.map((s) => ({ sourceId: s.sourceId, sourceSeriesId: s.sourceSeriesId }))
    if (refs.length === 0) {
      membership = {}
      return
    }
    let cancelled = false
    getLibraryMembership(refs)
      .then((rows) => {
        if (cancelled) return
        const map: Record<string, string> = {}
        for (const r of rows) map[key(r.sourceId, r.sourceSeriesId)] = r.libraryId
        membership = map
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  })
</script>

<div class="grid grid-cols-[repeat(auto-fill,minmax(160px,1fr))] gap-4">
  {#each series as s (`${s.sourceId}/${s.sourceSeriesId}`)}
    <SeriesCard
      series={s}
      sourceLabel={sourceLabels?.[s.sourceId]}
      libraryId={membership[key(s.sourceId, s.sourceSeriesId)]}
    />
  {/each}
</div>
