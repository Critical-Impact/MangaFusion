<script lang="ts">
  import { onMount } from 'svelte'
  import ContinueReading from '../lib/ContinueReading.svelte'
  import RecentDownloads from '../lib/RecentDownloads.svelte'
  import RecentlyUpdated from '../lib/RecentlyUpdated.svelte'
  import CollectionRail from '../lib/CollectionRail.svelte'
  import { getCollections, type Collection } from '../lib/api'
  import { resolveItems } from '../lib/dashboard.svelte'

  // Collections drive any collection rails on the dashboard. The built-in rails render regardless, so
  // this is best-effort — a failed load just means no collection rails this session.
  let collections = $state<Collection[]>([])
  onMount(async () => {
    try {
      collections = await getCollections()
    } catch {
      /* built-in rails still render */
    }
  })

  const items = $derived(resolveItems(collections).filter((i) => i.visible))
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  {#each items as item (item.key)}
    {#if item.type === 'rail'}
      {#if item.key === 'continue-reading'}
        <ContinueReading />
      {:else if item.key === 'recent-downloads'}
        <RecentDownloads />
      {:else if item.key === 'recently-updated'}
        <RecentlyUpdated />
      {/if}
    {:else}
      <CollectionRail id={item.key} title={item.label} />
    {/if}
  {/each}
</section>
