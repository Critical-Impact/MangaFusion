<script lang="ts">
  import { onMount } from 'svelte'
  import { getTags } from '../lib/api'
  import ResultsBrowser from '../lib/ResultsBrowser.svelte'

  let { params } = $props<{ params: { sourceId: string; tagId: string } }>()

  let name = $state('Genre')

  onMount(async () => {
    try {
      const tags = await getTags(params.sourceId)
      name = tags.find((t) => t.id === params.tagId)?.name ?? 'Genre'
    } catch {
      /* name is best-effort */
    }
  })
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-4 text-[1.4rem]">{name}</h1>
  <ResultsBrowser sourceId={params.sourceId} tag={params.tagId} placeholder={`Search in ${name}…`} />
</section>
