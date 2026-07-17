<script lang="ts">
  import { getCollection, type CollectionMember } from './api'
  import CardRail from './CardRail.svelte'
  import PosterCard from './PosterCard.svelte'
  import { BestEffortList } from './bestEffortList.svelte'

  let { id, title }: { id: string; title: string } = $props()

  const list = new BestEffortList(async () => (await getCollection(id, true)).members)
</script>

<CardRail {title} href={`/collections/${id}`} items={list.items} key={(m) => m.seriesId}>
  {#snippet card(m: CollectionMember)}
    <div class="w-40 min-w-0 shrink-0">
      <PosterCard title={m.title} coverUrl={m.coverUrl} href={`/library/${m.seriesId}`} />
    </div>
  {/snippet}
</CardRail>
