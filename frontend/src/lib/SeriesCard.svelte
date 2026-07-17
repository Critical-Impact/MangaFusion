<script lang="ts">
  import { push } from 'svelte-spa-router'
  import { addToLibrary, getChapters, type Series } from './api'
  import { notify } from './notify'
  import PosterCard from './PosterCard.svelte'

  let { series, sourceLabel }: { series: Series; sourceLabel?: string } = $props()

  let adding = $state(false)
  let added = $state(false)
  let previewing = $state(false)

  function open() {
    push(`/series/${series.sourceId}/${series.sourceSeriesId}`)
  }

  // Jump straight into the first available chapter, read live from the source (no library add). Needs
  // a chapter lookup since the card only carries series-level data.
  async function preview(e: MouseEvent) {
    e.stopPropagation()
    previewing = true
    try {
      const langs = series.availableTranslatedLanguages
      const lang = langs.includes('en') ? 'en' : langs[0]
      const page = await getChapters(series.sourceId, series.sourceSeriesId, {
        lang: lang ? [lang] : [],
        order: 'asc',
        limit: 1,
        includeExternal: false,
      })
      const first = page.items[0]
      if (!first) {
        notify.error('No readable chapters to preview.')
        return
      }
      const from = location.hash.slice(1) || '/browse'
      const q = new URLSearchParams({ from, seriesId: series.sourceSeriesId, title: series.title })
      if (lang) q.set('lang', lang)
      if (first.number) q.set('num', first.number)
      if (first.volume) q.set('vol', first.volume)
      push(`/preview/${series.sourceId}/${first.sourceChapterId}?${q.toString()}`)
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to open preview.')
    } finally {
      previewing = false
    }
  }

  async function add(e: MouseEvent) {
    e.stopPropagation()
    adding = true
    try {
      await addToLibrary(series.sourceId, series.sourceSeriesId)
      added = true
    } catch {
      /* surfaced on the series page if it matters */
    } finally {
      adding = false
    }
  }
</script>

<PosterCard
  title={series.title}
  subtitle={`${series.status}${series.year ? ` · ${series.year}` : ''}`}
  coverUrl={series.coverUrl}
  onclick={open}
>
  {#snippet overlay()}
    <div
      class="absolute inset-0 flex flex-col justify-end gap-[0.4rem] p-[0.6rem] opacity-0 transition-opacity duration-150 [background:linear-gradient(to_top,rgba(0,0,0,0.92)_40%,rgba(0,0,0,0.1))] group-hover:opacity-100 group-focus-visible:opacity-100"
    >
      {#if series.description}
        <p class="m-0 line-clamp-6 text-[0.72rem] leading-[1.3] text-text-2">{series.description}</p>
      {/if}
      {#if series.tags.length}
        <div class="flex flex-wrap gap-[0.25rem]">
          {#each series.tags.slice(0, 6) as t (t.id ?? t.name)}
            <span class="rounded-[var(--r-pill)] bg-white/15 px-[0.35rem] py-[0.05rem] text-[0.62rem] text-foreground">{t.name}</span>
          {/each}
        </div>
      {/if}
    </div>
    {#if series.lastChapter}
      <span
        class="absolute top-[0.4rem] left-[0.4rem] rounded-[var(--r-pill)] bg-black/[0.72] px-[0.4rem] py-[0.12rem] text-[0.68rem] font-semibold text-foreground"
        title="Last chapter on the source"
      >
        Ch. {series.lastChapter}
      </span>
    {/if}
    {#if sourceLabel}
      <span
        class="absolute bottom-[0.4rem] left-[0.4rem] max-w-[calc(100%-0.8rem)] truncate rounded-[var(--r-pill)] bg-primary/[0.85] px-[0.4rem] py-[0.12rem] text-[0.62rem] font-semibold text-white"
        title={`Source: ${sourceLabel}`}
      >
        {sourceLabel}
      </span>
    {/if}
    <button
      class={`pointer-events-auto absolute top-[0.4rem] right-[0.4rem] grid h-[1.7rem] w-[1.7rem] cursor-pointer place-items-center rounded-[var(--r-pill)] border-0 text-[1rem] leading-none font-bold text-white disabled:cursor-default ${added ? 'bg-green-500/[0.92]' : 'bg-primary/[0.92]'}`}
      onclick={add}
      disabled={adding || added}
      title={added ? 'In library' : 'Add to library'}
    >
      {added ? '✓' : adding ? '…' : '+'}
    </button>
    <button
      class="pointer-events-auto absolute top-[2.4rem] right-[0.4rem] grid h-[1.7rem] w-[1.7rem] cursor-pointer place-items-center rounded-[var(--r-pill)] border-0 bg-black/[0.6] text-[0.85rem] leading-none text-white disabled:cursor-default"
      onclick={preview}
      disabled={previewing}
      title="Preview first chapter (read without adding)"
    >
      {previewing ? '…' : '▶'}
    </button>
  {/snippet}
</PosterCard>
