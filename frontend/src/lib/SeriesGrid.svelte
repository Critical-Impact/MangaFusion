<script lang="ts">
  import SeriesCard from './SeriesCard.svelte'
  import type { Series } from './api'

  // sourceLabels (id → display name) is passed in the "All sources" view so each card can show which
  // source it came from; undefined for a single-source browse.
  let { series, sourceLabels }: { series: Series[]; sourceLabels?: Record<string, string> } = $props()
</script>

<div class="grid grid-cols-[repeat(auto-fill,minmax(160px,1fr))] gap-4">
  {#each series as s (`${s.sourceId}/${s.sourceSeriesId}`)}
    <SeriesCard series={s} sourceLabel={sourceLabels?.[s.sourceId]} />
  {/each}
</div>
