<script lang="ts">
  import { onMount, onDestroy } from 'svelte'
  import { push, router } from 'svelte-spa-router'
  import { fly } from 'svelte/transition'
  import { cubicOut } from 'svelte/easing'
  import {
    getChapterManifest,
    getChapters,
    getNeighbors,
    getPreviewManifest,
    pageUrl,
    previewPageUrl,
    saveProgress,
    type Chapter,
    type ChapterManifest,
    type ReaderNeighbors,
  } from '../lib/api'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // Two modes on one component: library reading (`/read/:chapterId`) and a stateless source preview
  // (`/preview/:sourceId/:chapterId`). Preview reads pages live from the source, tracks no progress,
  // and has no chapter neighbours — discriminated by the presence of `sourceId`.
  let { params } = $props<{ params: { chapterId: string; sourceId?: string } }>()
  const isPreview = $derived(!!params.sourceId)

  let manifest = $state<ChapterManifest | null>(null)
  let neighbors = $state<ReaderNeighbors>({ prevChapterId: null, nextChapterId: null })
  // Preview mode only: the source's ordered chapter list (current language), so reaching the end of a
  // chapter can advance to the next one. Fetched per series, not per chapter.
  let previewChapters: Chapter[] = []
  let loading = $state(true)
  let error = $state('')

  let page = $state(0)
  let mode = $state<'paged' | 'webtoon'>('paged')
  let direction = $state<'ltr' | 'rtl'>('ltr')
  let fit = $state<'width' | 'height'>('height')
  let chrome = $state(true)
  let showHelp = $state(false)
  let isFullscreen = $state(false)
  let readerEl: HTMLElement | undefined = $state()

  // Where to go when leaving the reader — wherever the user actually launched it from (Home's rails,
  // a filtered library view, etc.), passed as a ?from= query param on the initial /read/:chapterId
  // link. Captured once on mount since this component instance is reused across next/prev chapter
  // navigation (see the params.chapterId effect below), so it survives that in-reader navigation.
  // Falls back to the series page for direct/deep links that carry no ?from=.
  let entryPath: string | null = null

  let dirTouched = false
  let pageEls: HTMLElement[] = []
  let webtoonWantsScroll = false

  // Directional page-turn slide (paged mode only).
  let navDir = $state(1) // +1 = forward (next), -1 = backward (prev)
  let animatePage = $state(false) // suppressed on chapter load / mode toggle so only in-chapter page turns slide
  let lastPage = 0 // baseline for computing the slider's net drag direction

  const reduceMotion =
    typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches
  const SLIDE_DUR = 200
  function slideDist() {
    return Math.round(window.innerWidth * 0.18)
  }
  // Forward in LTR pushes content leftward (new page enters from the right); RTL mirrors.
  const slideSign = $derived(navDir * (direction === 'rtl' ? -1 : 1))
  const slideParams = $derived(animatePage && !reduceMotion ? { dist: slideSign * slideDist(), dur: SLIDE_DUR } : { dist: 0, dur: 0 })

  const PREFS_KEY = 'mf-reader-prefs'

  function loadPrefs(): Record<string, unknown> {
    try {
      const p = JSON.parse(localStorage.getItem(PREFS_KEY) || '{}')
      if (p.mode === 'paged' || p.mode === 'webtoon') mode = p.mode
      if (p.direction === 'ltr' || p.direction === 'rtl') direction = p.direction
      if (p.fit === 'width' || p.fit === 'height') fit = p.fit
      return p
    } catch {
      return {}
    }
  }
  function savePrefs() {
    localStorage.setItem(PREFS_KEY, JSON.stringify({ mode, direction, fit }))
  }

  onMount(async () => {
    const prefs = loadPrefs()
    dirTouched = 'direction' in prefs
    entryPath = new URLSearchParams(router.querystring).get('from')
    await loadChapter(params.chapterId)
    window.addEventListener('keydown', onKey)
    document.addEventListener('visibilitychange', onHide)
    document.addEventListener('fullscreenchange', onFullscreenChange)
  })

  onDestroy(() => {
    window.removeEventListener('keydown', onKey)
    document.removeEventListener('visibilitychange', onHide)
    document.removeEventListener('fullscreenchange', onFullscreenChange)
    flushNow()
  })

  // Navigating to prev/next chapter re-uses this route with a new param.
  $effect(() => {
    const id = params.chapterId
    if (manifest && manifest.chapterId !== id) loadChapter(id)
  })

  async function loadChapter(id: string) {
    loading = true
    error = ''
    try {
      if (isPreview && params.sourceId) {
        // Series title / chapter number / series id ride along as query params from the launcher, so
        // the header renders without an extra series fetch. Progress + neighbours don't apply.
        const m = await getPreviewManifest(params.sourceId, id)
        const q = new URLSearchParams(router.querystring)
        const seriesId = q.get('seriesId') ?? ''
        const lang = q.get('lang') ?? ''
        manifest = {
          chapterId: id,
          artifactId: '',
          pageCount: m.pageCount,
          startPageIndex: 0,
          readingDirection: m.readingDirection,
          seriesId,
          seriesTitle: q.get('title') ?? 'Preview',
          number: q.get('num'),
          volume: q.get('vol'),
          language: lang,
        }
        neighbors = await loadPreviewNeighbors(params.sourceId, seriesId, lang, id)
      } else {
        manifest = await getChapterManifest(id)
        neighbors = await getNeighbors(id)
      }
      if (!dirTouched) direction = manifest.readingDirection
      animatePage = false
      page = Math.min(Math.max(manifest.startPageIndex, 0), Math.max(manifest.pageCount - 1, 0))
      lastPage = page
      webtoonWantsScroll = mode === 'webtoon' && page > 0
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to open chapter.'
    } finally {
      loading = false
    }
  }

  // Preview neighbours: fetch the source's ordered chapter list (same language filter Series.svelte
  // uses) and locate the current chapter. Best-effort — a failure just means no prev/next, never a
  // broken read.
  async function loadPreviewNeighbors(
    sourceId: string,
    seriesId: string,
    lang: string,
    id: string,
  ): Promise<ReaderNeighbors> {
    previewChapters = []
    if (!seriesId) return { prevChapterId: null, nextChapterId: null }
    try {
      const page = await getChapters(sourceId, seriesId, {
        lang: lang ? [lang] : [],
        order: 'asc',
        limit: 500,
        includeExternal: false,
      })
      previewChapters = page.items
    } catch {
      return { prevChapterId: null, nextChapterId: null }
    }
    const idx = previewChapters.findIndex((c) => c.sourceChapterId === id)
    return {
      prevChapterId: idx > 0 ? previewChapters[idx - 1].sourceChapterId : null,
      nextChapterId: idx >= 0 && idx < previewChapters.length - 1 ? previewChapters[idx + 1].sourceChapterId : null,
    }
  }

  // The prev/next chapter in the preview list, or null at the ends.
  function previewNeighbor(dir: 1 | -1): Chapter | null {
    if (!manifest) return null
    const idx = previewChapters.findIndex((c) => c.sourceChapterId === manifest!.chapterId)
    if (idx < 0) return null
    const j = idx + dir
    return j >= 0 && j < previewChapters.length ? previewChapters[j] : null
  }

  // Build the /preview URL for a neighbouring chapter, carrying from/seriesId/lang/title forward and
  // swapping in the new chapter's number/volume for the header.
  function previewUrlFor(c: Chapter): string {
    const q = new URLSearchParams(router.querystring)
    if (c.number) q.set('num', c.number)
    else q.delete('num')
    if (c.volume) q.set('vol', c.volume)
    else q.delete('vol')
    return `/preview/${params.sourceId}/${c.sourceChapterId}?${q.toString()}`
  }

  // Page image URL — library artifact bytes vs. the source-page proxy, by mode.
  function pageSrc(index: number): string {
    if (!manifest) return ''
    return isPreview && params.sourceId
      ? previewPageUrl(params.sourceId, manifest.chapterId, index)
      : pageUrl(manifest.chapterId, index)
  }

  // --- Progress persistence (debounced + flush on hide/unload) --------------------------------
  let saveTimer: ReturnType<typeof setTimeout> | null = null
  function scheduleSave() {
    if (isPreview || !manifest) return // preview is stateless — no progress
    if (saveTimer) clearTimeout(saveTimer)
    const id = manifest.chapterId
    const idx = page
    const done = idx >= manifest.pageCount - 1
    saveTimer = setTimeout(() => {
      saveTimer = null
      saveProgress(id, idx, done).catch(() => {})
    }, 700)
  }
  function flushNow() {
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
    if (isPreview || !manifest) return // preview is stateless — no progress
    const done = page >= manifest.pageCount - 1
    // keepalive so the request survives the page being torn down.
    fetch(`/api/library/chapters/${manifest.chapterId}/progress`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      keepalive: true,
      body: JSON.stringify({ pageIndex: page, completed: done }),
    }).catch(() => {})
  }
  function onHide() {
    if (document.visibilityState === 'hidden') flushNow()
  }

  // --- Navigation -----------------------------------------------------------------------------
  function next() {
    if (!manifest) return
    if (page < manifest.pageCount - 1) {
      navDir = 1
      animatePage = true
      page += 1
      lastPage = page
      scheduleSave()
    } else {
      nextChapter()
    }
  }
  function prev() {
    if (!manifest) return
    if (page > 0) {
      navDir = -1
      animatePage = true
      page -= 1
      lastPage = page
      scheduleSave()
    } else {
      prevChapter()
    }
  }
  function nextChapter() {
    if (isPreview) {
      const next = previewNeighbor(1)
      if (next) push(previewUrlFor(next))
      else exit()
      return
    }
    if (manifest) saveProgress(manifest.chapterId, manifest.pageCount - 1, true).catch(() => {})
    if (neighbors.nextChapterId) push(`/read/${neighbors.nextChapterId}`)
    else exit()
  }
  function prevChapter() {
    if (isPreview) {
      const prev = previewNeighbor(-1)
      if (prev) push(previewUrlFor(prev))
      return
    }
    if (neighbors.prevChapterId) push(`/read/${neighbors.prevChapterId}`)
  }
  function exit() {
    if (entryPath) push(entryPath)
    else if (isPreview) push(params.sourceId && manifest?.seriesId ? `/series/${params.sourceId}/${manifest.seriesId}` : '/browse')
    else if (manifest) push(`/library/${manifest.seriesId}`)
  }

  // Header title link target: the source series page in preview, the library series page otherwise.
  const seriesHref = $derived(
    !manifest
      ? '#'
      : isPreview
        ? params.sourceId && manifest.seriesId
          ? `/#/series/${params.sourceId}/${manifest.seriesId}`
          : '/#/browse'
        : `/#/library/${manifest.seriesId}`,
  )

  function toggleFullscreen() {
    if (!document.fullscreenElement) {
      readerEl?.requestFullscreen?.().catch(() => {})
    } else {
      document.exitFullscreen?.().catch(() => {})
    }
  }
  function onFullscreenChange() {
    isFullscreen = !!document.fullscreenElement
  }

  function toggleMode() {
    mode = mode === 'paged' ? 'webtoon' : 'paged'
    if (mode === 'webtoon' && page > 0) webtoonWantsScroll = true
    else if (mode === 'paged') animatePage = false // re-entering paged mode shouldn't slide in
    savePrefs()
  }

  // The range input's bind:value updates `page` continuously; `change` fires once the interaction
  // ends, so lastPage (untouched during the drag) still holds the pre-drag value here.
  function onSliderChange() {
    navDir = page >= lastPage ? 1 : -1
    animatePage = true
    lastPage = page
    scheduleSave()
  }
  function toggleFit() {
    fit = fit === 'width' ? 'height' : 'width'
    savePrefs()
  }
  function toggleDirection() {
    direction = direction === 'ltr' ? 'rtl' : 'ltr'
    dirTouched = true
    savePrefs()
  }

  function onKey(e: KeyboardEvent) {
    if (showHelp && e.key !== '?' && e.key !== 'Escape') return
    switch (e.key) {
      case 'ArrowRight': direction === 'rtl' ? prev() : next(); break
      case 'ArrowLeft': direction === 'rtl' ? next() : prev(); break
      case ' ':
      case 'ArrowDown':
        if (mode === 'paged') { next(); e.preventDefault() }
        break
      case 'ArrowUp':
        if (mode === 'paged') { prev(); e.preventDefault() }
        break
      case 'n': nextChapter(); break
      case 'p': prevChapter(); break
      case 'f': toggleFit(); break
      case 'w': toggleMode(); break
      case '?': showHelp = !showHelp; break
      case 'Escape': showHelp ? (showHelp = false) : exit(); break
    }
  }

  // Paged mode: tap zones. Left visual third / right visual third page; center toggles chrome.
  function onTap(e: MouseEvent) {
    const x = e.clientX / window.innerWidth
    if (x < 0.33) direction === 'rtl' ? next() : prev()
    else if (x > 0.67) direction === 'rtl' ? prev() : next()
    else chrome = !chrome
  }

  // Preload upcoming pages in paged mode.
  $effect(() => {
    if (!manifest || mode !== 'paged') return
    for (let d = 1; d <= 3; d++) {
      const i = page + d
      if (i < manifest.pageCount) {
        const img = new Image()
        img.src = pageSrc(i)
      }
    }
  })

  // A fit:width page can be taller than the viewport (scrollable — see .stage below); each new page
  // should start scrolled to its own top rather than inheriting the previous page's scroll offset.
  let stageEl: HTMLElement | undefined = $state()
  $effect(() => {
    page
    if (mode === 'paged' && stageEl) stageEl.scrollTop = 0
  })

  // Webtoon mode: track the centered page for progress, and scroll to the resume page once.
  $effect(() => {
    if (mode !== 'webtoon' || !manifest || loading) return
    const io = new IntersectionObserver(
      (entries) => {
        for (const en of entries) {
          if (!en.isIntersecting) continue
          const idx = Number((en.target as HTMLElement).dataset.index)
          if (!Number.isNaN(idx)) {
            page = idx
            scheduleSave()
          }
        }
      },
      { rootMargin: '-45% 0px -45% 0px' },
    )
    for (const el of pageEls) if (el) io.observe(el)

    if (webtoonWantsScroll) {
      webtoonWantsScroll = false
      queueMicrotask(() => pageEls[page]?.scrollIntoView({ block: 'start' }))
    }
    return () => io.disconnect()
  })

  const label = $derived(
    manifest
      ? [manifest.volume ? `Vol. ${manifest.volume}` : '', manifest.number ? `Ch. ${manifest.number}` : '']
          .filter(Boolean)
          .join(' ') || 'Oneshot'
      : '',
  )

  // Top/bottom chrome bars slide out of view (and lose their padding/border) when chrome is hidden —
  // ported from a descendant-selector rule (`.hidechrome .bar.top` etc.), now computed per bar since
  // each bar's hidden-state transform direction differs.
  function barClass(edge: 'top' | 'bottom', hidden: boolean): string {
    const base =
      'flex items-center gap-[0.4rem] z-[2] overflow-hidden bg-[rgba(20,20,27,0.95)] px-[0.7rem] [transition:opacity_0.2s,transform_0.2s,max-height_0.2s,padding_0.2s,border-color_0.2s]'
    const border = edge === 'top' ? 'border-b' : 'border-t'
    if (hidden) {
      const translate = edge === 'top' ? '-translate-y-full' : 'translate-y-full'
      return `${base} ${border} border-transparent py-0 max-h-0 opacity-0 ${translate} pointer-events-none`
    }
    return `${base} ${border} border-border-dim py-[0.4rem] max-h-[3rem]`
  }

  function icoClass(on = false): string {
    const base = 'min-w-[2rem] h-8 cursor-pointer rounded-[7px] border bg-surface-3 px-2 text-[0.85rem] disabled:cursor-default disabled:opacity-[0.35]'
    return on
      ? `${base} border-brand-soft text-foreground`
      : `${base} border-input text-text-dim hover:border-brand-soft hover:text-foreground`
  }
</script>

<div class="fixed inset-0 z-50 flex flex-col bg-bg-reader" bind:this={readerEl}>
  {#if loading}
    <p class="m-auto flex items-center gap-2 text-center text-text-mute"><Spinner />Loading…</p>
  {:else if error}
    <div class="m-auto flex max-w-[24rem] flex-col items-center gap-4 text-center">
      <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
      <Button onclick={exit}>Back to series</Button>
    </div>
  {:else if manifest}
    <header class={barClass('top', !chrome)}>
      <button class={icoClass()} onclick={exit} title="Back to series (Esc)">←</button>
      <div class="flex min-w-0 flex-col leading-[1.1]">
        <a href={seriesHref} class="truncate text-[0.85rem] font-semibold text-foreground no-underline">
          {manifest.seriesTitle}
        </a>
        <span class="text-[0.72rem] text-text-mute">{label}</span>
      </div>
      <span class="flex-1"></span>
      <span class="text-[0.8rem] text-text-dim tabular-nums">{page + 1} / {manifest.pageCount}</span>
      <button class={icoClass(mode === 'webtoon')} onclick={toggleMode} title="Toggle webtoon (w)">☰</button>
      <button class={icoClass()} onclick={toggleFit} title="Fit width/height (f)">{fit === 'width' ? '↔' : '↕'}</button>
      {#if mode === 'paged'}
        <button class={icoClass()} onclick={toggleDirection} title="Reading direction">{direction === 'rtl' ? 'RTL' : 'LTR'}</button>
      {/if}
      <button class={icoClass(isFullscreen)} onclick={toggleFullscreen} title="Toggle fullscreen">⛶</button>
      <button class={icoClass()} onclick={() => (showHelp = true)} title="Help (?)">?</button>
    </header>

    {#if mode === 'paged'}
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto" bind:this={stageEl} onclick={onTap}>
        <div
          class="grid h-full place-items-center [grid-template-columns:1fr] [grid-template-rows:1fr]"
          style={fit === 'width' ? 'align-items: start; align-items: safe center;' : ''}
        >
          {#key page}
            <img
              class={`[grid-area:1/1] block min-h-0 min-w-0 select-none ${
                fit === 'height' ? 'max-h-full max-w-full object-contain' : 'h-auto w-full'
              }`}
              src={pageSrc(page)}
              alt={`Page ${page + 1}`}
              draggable="false"
              in:fly={{ x: slideParams.dist, duration: slideParams.dur, easing: cubicOut }}
              out:fly={{ x: -slideParams.dist, duration: slideParams.dur, easing: cubicOut }}
            />
          {/key}
        </div>
      </div>
    {:else}
      <div class="flex flex-1 flex-col items-center overflow-y-auto">
        {#each Array(manifest.pageCount) as _, i (i)}
          <img
            bind:this={pageEls[i]}
            data-index={i}
            class="block h-auto w-full max-w-[900px] select-none"
            src={pageSrc(i)}
            alt={`Page ${i + 1}`}
            loading="lazy"
            draggable="false"
          />
        {/each}
        <div class="p-8 text-center">
          {#if neighbors.nextChapterId}
            <Button onclick={nextChapter}>Next chapter →</Button>
          {:else}
            <p class="muted">{isPreview ? 'End of preview.' : 'End of the last downloaded chapter.'}</p>
          {/if}
        </div>
      </div>
    {/if}

    <footer class={barClass('bottom', !chrome)}>
      <button class={icoClass()} onclick={prevChapter} disabled={!neighbors.prevChapterId} title="Previous chapter (p)">⏮</button>
      {#if mode === 'paged'}
        <button class={icoClass()} onclick={prev} title="Previous page">‹</button>
        <input
          class="flex-1 accent-brand-soft"
          type="range"
          min="0"
          max={manifest.pageCount - 1}
          bind:value={page}
          onchange={onSliderChange}
        />
        <button class={icoClass()} onclick={next} title="Next page">›</button>
      {:else}
        <span class="flex-1"></span>
      {/if}
      <button class={icoClass()} onclick={nextChapter} disabled={!neighbors.nextChapterId} title="Next chapter (n)">⏭</button>
    </footer>

    {#if showHelp}
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="absolute inset-0 z-[5] flex items-center justify-center bg-black/60" onclick={() => (showHelp = false)}>
        <div class="max-w-[380px] rounded-[var(--r-lg)] border border-border bg-surface-2 px-6 py-[1.2rem]">
          <h3 class="mb-[0.7rem]">Reader shortcuts</h3>
          <ul class="mb-4 pl-[1.1rem] text-[0.85rem] leading-[1.6] text-text-dim">
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">←</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">→</kbd> — page (respects direction)</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">Space</kbd> — next page · <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">↑</kbd>/<kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">↓</kbd> — page</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">n</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">p</kbd> — next / previous chapter</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">w</kbd> — paged ↔ webtoon · <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">f</kbd> — fit width/height</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">Esc</kbd> — back to series</li>
            <li>Tap left/right thirds to page, center to hide chrome</li>
          </ul>
          <Button onclick={() => (showHelp = false)}>Close</Button>
        </div>
      </div>
    {/if}
  {/if}
</div>
