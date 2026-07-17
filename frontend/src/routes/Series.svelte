<script lang="ts">
  import { onMount } from 'svelte'
  import { push, link } from 'svelte-spa-router'
  import { getSeries, getChapters, addToLibrary, sourceSeriesUrl, authorHref, seriesTagHref, type Series, type Chapter } from '../lib/api'
  import { notify } from '../lib/notify'
  import Cover from '../lib/Cover.svelte'
  import FilterBar from '../lib/FilterBar.svelte'
  import { ensureLanguagesLoaded, languageName } from '../lib/languages.svelte'

  ensureLanguagesLoaded()
  import { Button } from '../lib/components/ui/button/index.js'
  import { Checkbox } from '../lib/components/ui/checkbox/index.js'
  import { Label } from '../lib/components/ui/label/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Badge, badgeVariants } from '../lib/components/ui/badge/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'
  import { cn } from '../lib/utils.js'

  let { params } = $props<{ params: { sourceId: string; seriesId: string } }>()

  let series = $state<Series | null>(null)
  let chapters = $state<Chapter[]>([])
  let lang = $state('en')
  let includeExternal = $state(false)
  let loadingSeries = $state(true)
  let loadingChapters = $state(false)
  let adding = $state(false)

  async function addLibrary() {
    if (!series) return
    adding = true
    try {
      const { id } = await addToLibrary(series.sourceId, series.sourceSeriesId)
      push(`/library/${id}`)
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to add to library.')
    } finally {
      adding = false
    }
  }
  let error = $state('')

  onMount(async () => {
    try {
      series = await getSeries(params.sourceId, params.seriesId)
      const available = series.availableTranslatedLanguages
      if (available.length && !available.includes(lang)) {
        lang = available.includes('en') ? 'en' : available[0]
      }
      await loadChapters()
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to load series.'
    } finally {
      loadingSeries = false
    }
  })

  async function loadChapters() {
    loadingChapters = true
    try {
      const page = await getChapters(params.sourceId, params.seriesId, {
        lang: lang ? [lang] : [],
        order: 'asc',
        limit: 500,
        includeExternal,
      })
      chapters = page.items
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to load chapters.')
    } finally {
      loadingChapters = false
    }
  }

  function formatDate(iso: string | null): string {
    if (!iso) return ''
    return new Date(iso).toLocaleDateString()
  }

  function chapterLabel(c: Chapter): string {
    const parts: string[] = []
    if (c.volume) parts.push(`Vol. ${c.volume}`)
    if (c.number) parts.push(`Ch. ${c.number}`)
    if (parts.length === 0) parts.push('Oneshot')
    return parts.join(' ')
  }

  // Read a chapter live from the source without adding to the library (external chapters have no
  // resolvable pages). Series title / number / id ride along so the reader header needs no extra fetch.
  function previewChapter(c: Chapter) {
    if (c.isExternal) return
    const q = new URLSearchParams({ from: `/series/${params.sourceId}/${params.seriesId}`, seriesId: params.seriesId })
    if (lang) q.set('lang', lang)
    if (series) q.set('title', series.title)
    if (c.number) q.set('num', c.number)
    if (c.volume) q.set('vol', c.volume)
    push(`/preview/${params.sourceId}/${c.sourceChapterId}?${q.toString()}`)
  }

  // First readable (non-external) chapter in the loaded, ordered list — backs the header Preview button.
  const firstPreviewable = $derived(chapters.find((c) => !c.isExternal) ?? null)
  function previewFirst() {
    if (firstPreviewable) previewChapter(firstPreviewable)
  }
</script>

{#if loadingSeries}
  <p class="muted flex items-center gap-2 px-5 py-8"><Spinner />Loading…</p>
{:else if error && !series}
  <div class="px-5 py-8">
    <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
  </div>
{:else if series}
  <section class="mx-auto max-w-[1100px] px-5 py-6">
    <div class="mb-6 flex gap-6 max-[640px]:flex-col">
      <div class="flex-[0_0_180px]" style="--cover-w:180px">
        <Cover src={series.coverUrl} alt={series.title} />
      </div>
      <div class="min-w-0 flex-1">
        <h1 class="mb-[0.4rem] text-[1.5rem]">{series.title}</h1>
        <p class="muted">
          {series.status}{series.year ? ` · ${series.year}` : ''} · {series.contentRating}
        </p>
        <div class="mt-[0.6rem] flex flex-wrap items-center gap-[0.8rem]">
          <Button onclick={addLibrary} disabled={adding}>
            {#if adding}<Spinner />{/if}
            {adding ? 'Adding…' : '+ Add to library'}
          </Button>
          <Button
            variant="secondary"
            onclick={previewFirst}
            disabled={loadingChapters || !firstPreviewable}
            title="Read the first available chapter without adding to your library"
          >
            Preview
          </Button>
          {#if sourceSeriesUrl(series.sourceId, series.sourceSeriesId)}
            <a
              class="text-[0.85rem] text-brand-soft no-underline hover:underline"
              href={sourceSeriesUrl(series.sourceId, series.sourceSeriesId)}
              target="_blank"
              rel="noreferrer noopener"
            >
              View on MangaDex ↗
            </a>
          {/if}
        </div>
        {#if series.authors.length}
          <p class="muted mt-[0.8rem] text-[0.85rem]">
            By {#each series.authors as a, i (a.id ?? a.name)}{#if i > 0}, {/if}<a
              class="text-inherit no-underline hover:text-brand-soft hover:underline"
              href={authorHref(a)}
              use:link
            >{a.name}</a>{/each}
          </p>
        {/if}
        {#if series.description}
          <p class="my-[0.8rem] max-h-[9rem] overflow-y-auto text-[0.9rem] text-text-dim [white-space:pre-line]">{series.description}</p>
        {/if}
        {#if series.tags.length}
          <div class="flex flex-wrap gap-1.5">
            {#each series.tags as tag (tag.id ?? tag.name)}
              {@const href = seriesTagHref(series.sourceId, tag)}
              {#if href}
                <a
                  class={cn(badgeVariants({ variant: 'secondary' }), 'no-underline hover:text-brand-soft hover:underline')}
                  {href}
                  use:link
                >{tag.name}</a>
              {:else}
                <span class={badgeVariants({ variant: 'secondary' })}>{tag.name}</span>
              {/if}
            {/each}
          </div>
        {/if}
      </div>
    </div>

    <FilterBar showSearch={false}>
      {#snippet filters()}
        <label class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
          Language
          <Select type="single" bind:value={lang} onValueChange={loadChapters}>
            <SelectTrigger>{languageName(lang)}</SelectTrigger>
            <SelectContent>
              {#each series?.availableTranslatedLanguages ?? [] as l}<SelectItem value={l} label={languageName(l)}>{languageName(l)}</SelectItem>{/each}
            </SelectContent>
          </Select>
        </label>
        <div class="flex flex-row items-center gap-[0.4rem] text-[0.8rem] text-text-dim">
          <Checkbox id="include-external" bind:checked={includeExternal} onCheckedChange={loadChapters} />
          <Label for="include-external">Include external</Label>
        </div>
      {/snippet}
    </FilterBar>

    {#if loadingChapters}
      <p class="muted flex items-center gap-2"><Spinner />Loading chapters…</p>
    {:else if chapters.length === 0}
      <p class="muted">No chapters for this language{includeExternal ? '' : ' (try “Include external”)'}.</p>
    {:else}
      <ul class="m-0 list-none overflow-hidden rounded-[var(--r-md)] border border-border p-0">
        {#each chapters as c (c.sourceChapterId)}
          <li
            class="grid grid-cols-[8rem_1fr_auto_auto_auto] items-center gap-4 border-b border-border-dim px-4 py-[0.6rem] text-[0.88rem] last:border-b-0 max-[640px]:grid-cols-[1fr_auto] max-[640px]:gap-y-[0.2rem]"
          >
            <span class="font-semibold">{chapterLabel(c)}</span>
            <span class="truncate">{c.title ?? ''}</span>
            <span class="muted max-[640px]:hidden">{c.scanlationGroups.join(', ')}</span>
            <span class="muted text-[0.8rem] max-[640px]:hidden">{formatDate(c.publishedAt)}</span>
            {#if c.isExternal}
              <Badge variant="outline" class="justify-self-end text-external border-external/40">external</Badge>
            {:else}
              <Button
                variant="secondary"
                size="mini"
                class="justify-self-end border-brand-soft/40 text-brand-soft hover:border-brand-soft"
                title="Read without adding to your library"
                onclick={() => previewChapter(c)}
              >
                Preview
              </Button>
            {/if}
          </li>
        {/each}
      </ul>
    {/if}
  </section>
{/if}
