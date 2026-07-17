<script lang="ts">
  import { link, router } from 'svelte-spa-router'
  import { getLibrary, type LibrarySeries } from '../lib/api'
  import Cover from '../lib/Cover.svelte'
  import ResultsBrowser from '../lib/ResultsBrowser.svelte'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  let { params } = $props<{ params: { sourceId: string; authorId: string } }>()

  let name = $state('Author')
  let libraryItems = $state<LibrarySeries[]>([])
  let libraryLoading = $state(true)

  const isLocal = $derived(params.sourceId === 'local')

  // Runs on mount and again whenever the route params change (svelte-spa-router can reuse this
  // component instance when navigating from one author page straight to another).
  $effect(() => {
    void params.sourceId
    void params.authorId
    const p = new URLSearchParams(router.querystring)
    name = p.get('name') || 'Author'
    loadLibrary()
  })

  async function loadLibrary() {
    libraryLoading = true
    try {
      const res = await getLibrary({
        authorSourceId: params.sourceId,
        authorId: params.authorId,
        limit: 24,
      })
      libraryItems = res.items
    } catch {
      libraryItems = []
    } finally {
      libraryLoading = false
    }
  }
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-5 text-[1.4rem]">{name}</h1>

  <section class="mb-7">
    <h2 class="mb-[0.7rem] text-base text-foreground">In your library</h2>
    {#if libraryLoading}
      <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
    {:else if libraryItems.length === 0}
      <p class="muted">Nothing by {name} in your library yet.</p>
    {:else}
      <div class="flex gap-[0.9rem] overflow-x-auto pb-[0.4rem] [scrollbar-width:thin]">
        {#each libraryItems as s (s.id)}
          <a
            class="group flex w-40 min-w-0 shrink-0 flex-col gap-[0.3rem] text-inherit no-underline"
            href={`/library/${s.id}`}
            use:link
            title={s.title}
          >
            <Cover src={s.coverUrl} alt="" />
            <span class="max-w-full truncate px-[var(--poster-pad)] text-[0.78rem] text-text-2 group-hover:text-white">
              {s.title}
            </span>
          </a>
        {/each}
      </div>
    {/if}
  </section>

  {#if !isLocal}
    <section>
      <h2 class="mb-[0.7rem] text-base text-foreground">On MangaDex</h2>
      <ResultsBrowser
        sourceId={params.sourceId}
        authorId={params.authorId}
        placeholder={`Search ${name}'s titles…`}
      />
    </section>
  {/if}
</section>
