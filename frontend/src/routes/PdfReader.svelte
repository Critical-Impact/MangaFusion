<script lang="ts">
  import { onMount, onDestroy } from 'svelte'
  import { push, router } from 'svelte-spa-router'
  import * as pdfjsLib from 'pdfjs-dist'
  import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
  import { getPdfManifest, getNeighbors, pdfUrl, savePdfProgress, type PdfManifest, type ReaderNeighbors } from '../lib/api'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // Self-hosted worker (bundled by Vite, same-origin — no CDN, CSP-safe).
  pdfjsLib.GlobalWorkerOptions.workerSrc = pdfWorkerUrl

  // The PDF sibling of Reader/TextReader: a fixed-layout reader for light-novel PDFs kept as-is, so the
  // cover, illustrations, headings and layout render exactly as the PDF has them. Continuous vertical
  // scroll of PDF.js-rendered pages, lazily rendered as they scroll into view.
  let { params } = $props<{ params: { chapterId: string } }>()

  let manifest = $state<PdfManifest | null>(null)
  let neighbors = $state<ReaderNeighbors>({ prevChapterId: null, nextChapterId: null })
  let loading = $state(true)
  let error = $state('')

  let chrome = $state(true)
  let showHelp = $state(false)
  let showToc = $state(false)
  let isFullscreen = $state(false)
  let readerEl: HTMLElement | undefined = $state()
  let scrollEl: HTMLElement | undefined = $state()
  let entryPath: string | null = null

  let numPages = $state(0)
  let page = $state(0) // 0-based, current/most-visible page
  let zoom = $state(1)
  let outline = $state<{ title: string; page: number }[]>([])

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  let pdf: any = null
  let baseAspect = 1 // page height / width, from page 1 (LN PDFs are uniform)
  let baseWidthPt = 0
  let pageEls = $state<HTMLElement[]>([])
  const rendered = new Set<number>()
  let resumePage = 0

  const label = $derived(
    manifest
      ? [manifest.volume ? `Vol. ${manifest.volume}` : '', manifest.number ? `Ch. ${manifest.number}` : '']
          .filter(Boolean)
          .join(' ') || 'PDF'
      : '',
  )

  // Fixed-layout: fit the page to a comfortable column width (bounded on desktop), times the zoom.
  function renderWidth(): number {
    const avail = Math.min(scrollEl?.clientWidth ?? window.innerWidth, 1100) - 24
    return Math.max(200, avail) * zoom
  }

  onMount(async () => {
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
    io?.disconnect()
    pdf?.destroy?.()
  })

  $effect(() => {
    const id = params.chapterId
    if (manifest && manifest.chapterId !== id) loadChapter(id)
  })

  async function loadChapter(id: string) {
    loading = true
    error = ''
    io?.disconnect()
    pdf?.destroy?.()
    pdf = null
    rendered.clear()
    pageEls = []
    try {
      const [m, n] = await Promise.all([getPdfManifest(id), getNeighbors(id)])
      manifest = m
      neighbors = n
      resumePage = m.startPage

      pdf = await pdfjsLib.getDocument({ url: pdfUrl(id), withCredentials: true }).promise
      numPages = pdf.numPages
      const first = await pdf.getPage(1)
      const vp = first.getViewport({ scale: 1 })
      baseWidthPt = vp.width
      baseAspect = vp.height / vp.width
      await loadOutline()
      loading = false
      // Let the placeholders lay out, then observe + jump to the resume page.
      requestAnimationFrame(() => {
        setupObserver()
        if (resumePage > 0 && pageEls[resumePage]) pageEls[resumePage].scrollIntoView()
      })
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to open PDF.'
      loading = false
    }
  }

  async function loadOutline() {
    outline = []
    try {
      const items = await pdf.getOutline()
      if (!items) return
      const flat: { title: string; page: number }[] = []
      for (const item of items) {
        const p = await destToPage(item.dest)
        if (p !== null) flat.push({ title: item.title, page: p })
      }
      outline = flat
    } catch {
      /* no outline — fine */
    }
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  async function destToPage(dest: any): Promise<number | null> {
    try {
      const resolved = typeof dest === 'string' ? await pdf.getDestination(dest) : dest
      if (!resolved) return null
      return await pdf.getPageIndex(resolved[0])
    } catch {
      return null
    }
  }

  function placeholderHeight(): number {
    return renderWidth() * baseAspect
  }

  async function renderPage(index: number) {
    if (rendered.has(index) || !pdf) return
    rendered.add(index)
    try {
      const p = await pdf.getPage(index + 1)
      const scale = renderWidth() / baseWidthPt
      const viewport = p.getViewport({ scale: scale * (window.devicePixelRatio || 1) })
      const canvas = document.createElement('canvas')
      canvas.width = viewport.width
      canvas.height = viewport.height
      canvas.style.width = '100%'
      canvas.style.height = 'auto'
      canvas.className = 'block'
      const ctx = canvas.getContext('2d')!
      await p.render({ canvasContext: ctx, viewport }).promise
      const host = pageEls[index]
      if (host) {
        host.replaceChildren(canvas)
        host.style.minHeight = ''
      }
    } catch {
      rendered.delete(index) // allow a retry on next scroll
    }
  }

  let io: IntersectionObserver | null = null
  function setupObserver() {
    io?.disconnect()
    io = new IntersectionObserver(
      (entries) => {
        for (const e of entries) {
          const idx = Number((e.target as HTMLElement).dataset.page)
          if (Number.isNaN(idx)) continue
          if (e.isIntersecting) {
            renderPage(idx)
            // Most-visible page drives progress.
            if (e.intersectionRatio > 0.5) {
              page = idx
              scheduleSave()
            }
          }
        }
      },
      { root: scrollEl, rootMargin: '400px 0px', threshold: [0, 0.5, 1] },
    )
    for (const el of pageEls) if (el) io.observe(el)
  }

  function rerenderAll() {
    // Zoom changed: drop rendered canvases and re-render the pages currently near view.
    rendered.clear()
    for (let i = 0; i < pageEls.length; i++) {
      const el = pageEls[i]
      if (!el) continue
      el.replaceChildren()
      el.style.minHeight = `${placeholderHeight()}px`
    }
    // The observer will re-fire renders for visible pages; nudge it.
    setupObserver()
  }

  function zoomBy(delta: number) {
    zoom = Math.min(3, Math.max(0.5, Math.round((zoom + delta) * 100) / 100))
    rerenderAll()
  }

  // --- progress (debounced + flush) ---
  let saveTimer: ReturnType<typeof setTimeout> | null = null
  function scheduleSave() {
    if (!manifest) return
    if (saveTimer) clearTimeout(saveTimer)
    const id = manifest.chapterId
    const p = page
    const done = p >= numPages - 1
    saveTimer = setTimeout(() => {
      saveTimer = null
      savePdfProgress(id, p, done).catch(() => {})
    }, 700)
  }
  function flushNow() {
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
    if (!manifest) return
    fetch(`/api/library/chapters/${manifest.chapterId}/pdf/progress`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      keepalive: true,
      body: JSON.stringify({ page, completed: page >= numPages - 1 }),
    }).catch(() => {})
  }
  function onHide() {
    if (document.visibilityState === 'hidden') flushNow()
  }

  function nextChapter() {
    if (manifest) savePdfProgress(manifest.chapterId, Math.max(0, numPages - 1), true).catch(() => {})
    if (neighbors.nextChapterId) push(`/read/${neighbors.nextChapterId}`)
    else exit()
  }
  function prevChapter() {
    if (neighbors.prevChapterId) push(`/read/${neighbors.prevChapterId}`)
  }
  function exit() {
    if (entryPath) push(entryPath)
    else if (manifest) push(`/library/${manifest.seriesId}`)
  }
  const seriesHref = $derived(manifest ? `/#/library/${manifest.seriesId}` : '#')

  function jumpTo(index: number) {
    showToc = false
    pageEls[index]?.scrollIntoView()
  }

  function toggleFullscreen() {
    if (!document.fullscreenElement) readerEl?.requestFullscreen?.().catch(() => {})
    else document.exitFullscreen?.().catch(() => {})
  }
  function onFullscreenChange() {
    isFullscreen = !!document.fullscreenElement
  }

  function onKey(e: KeyboardEvent) {
    if (showHelp && e.key !== '?' && e.key !== 'Escape') return
    switch (e.key) {
      case 'n': nextChapter(); break
      case 'p': prevChapter(); break
      case '+':
      case '=': zoomBy(0.1); break
      case '-': zoomBy(-0.1); break
      case '?': showHelp = !showHelp; break
      case 'Escape': showHelp ? (showHelp = false) : showToc ? (showToc = false) : exit(); break
    }
  }

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
    const base =
      'min-w-[2rem] h-8 cursor-pointer rounded-[7px] border bg-surface-3 px-2 text-[0.85rem] disabled:cursor-default disabled:opacity-[0.35]'
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
      <span class="text-[0.8rem] text-text-dim tabular-nums">{page + 1} / {numPages}</span>
      {#if outline.length}
        <button class={icoClass(showToc)} onclick={() => (showToc = !showToc)} title="Contents">☰</button>
      {/if}
      <button class={icoClass()} onclick={() => zoomBy(-0.1)} title="Zoom out (-)">−</button>
      <button class={icoClass()} onclick={() => zoomBy(0.1)} title="Zoom in (+)">+</button>
      <button class={icoClass(isFullscreen)} onclick={toggleFullscreen} title="Toggle fullscreen">⛶</button>
      <button class={icoClass()} onclick={() => (showHelp = true)} title="Help (?)">?</button>
    </header>

    <div class="relative min-h-0 flex-1">
      {#if showToc}
        <nav class="absolute top-0 bottom-0 left-0 z-[3] w-[18rem] max-w-[80%] overflow-y-auto border-r border-border bg-surface-2 py-2">
          {#each outline as item (item.title + item.page)}
            <button
              class="block w-full truncate px-4 py-[0.35rem] text-left text-[0.82rem] text-text-dim hover:bg-surface-3 hover:text-foreground"
              onclick={() => jumpTo(item.page)}
            >
              {item.title}
            </button>
          {/each}
        </nav>
      {/if}

      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="h-full overflow-y-auto bg-[#3a3a3f] py-4" bind:this={scrollEl}>
        <div class="mx-auto flex w-full max-w-[1100px] flex-col items-center gap-4 px-3">
          {#each Array(numPages) as _, i (i)}
            <div
              bind:this={pageEls[i]}
              data-page={i}
              class="w-full bg-white shadow-lg"
              style={`min-height: ${placeholderHeight()}px`}
            ></div>
          {/each}
        </div>
        <div class="p-8 text-center">
          {#if neighbors.nextChapterId}
            <Button onclick={nextChapter}>Next chapter →</Button>
          {:else}
            <p class="text-text-mute">End of the last downloaded chapter.</p>
          {/if}
        </div>
      </div>
    </div>

    <footer class={barClass('bottom', !chrome)}>
      <button class={icoClass()} onclick={prevChapter} disabled={!neighbors.prevChapterId} title="Previous chapter (p)">⏮</button>
      <span class="flex-1"></span>
      <button class={icoClass()} onclick={() => (chrome = !chrome)} title="Hide bars">▾</button>
      <span class="flex-1"></span>
      <button class={icoClass()} onclick={nextChapter} disabled={!neighbors.nextChapterId} title="Next chapter (n)">⏭</button>
    </footer>

    {#if showHelp}
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="absolute inset-0 z-[5] flex items-center justify-center bg-black/60" onclick={() => (showHelp = false)}>
        <div class="max-w-[380px] rounded-[var(--r-lg)] border border-border bg-surface-2 px-6 py-[1.2rem]">
          <h3 class="mb-[0.7rem]">PDF reader shortcuts</h3>
          <ul class="mb-4 pl-[1.1rem] text-[0.85rem] leading-[1.6] text-text-dim">
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">n</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">p</kbd> — next / previous chapter</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">+</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">−</kbd> — zoom · ☰ — contents</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">Esc</kbd> — back to series</li>
          </ul>
          <Button onclick={() => (showHelp = false)}>Close</Button>
        </div>
      </div>
    {/if}
  {/if}
</div>
