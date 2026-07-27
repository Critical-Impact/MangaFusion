<script lang="ts">
  import { onMount, onDestroy } from 'svelte'
  import { push, router } from 'svelte-spa-router'
  import {
    getProseManifest,
    getProseContent,
    getNeighbors,
    saveProseProgress,
    type ProseManifest,
    type ProseContent,
    type ReaderNeighbors,
  } from '../lib/api'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // The prose sibling of Reader.svelte: a real text reader for light novels. Continuous scroll only (no
  // pagination), scroll-fraction progress, its own typography prefs — but it reuses Reader's fullscreen
  // shell, tap-to-toggle chrome, help overlay, ?from= exit, and debounced-save-plus-flush progress.
  let { params } = $props<{ params: { chapterId: string } }>()

  let manifest = $state<ProseManifest | null>(null)
  let content = $state<ProseContent | null>(null)
  let neighbors = $state<ReaderNeighbors>({ prevChapterId: null, nextChapterId: null })
  let loading = $state(true)
  let error = $state('')

  let chrome = $state(true)
  let showHelp = $state(false)
  let isFullscreen = $state(false)
  let readerEl: HTMLElement | undefined = $state()
  let scrollEl: HTMLElement | undefined = $state()
  let articleEl: HTMLElement | undefined = $state()
  let progressPct = $state(0)
  let sectionInfo = $state({ current: 0, total: 0 })

  // Captured once on mount (survives in-reader next/prev navigation, which reuses this instance).
  let entryPath: string | null = null

  // Typography — the content column only, separate from the image reader's prefs object.
  const THEMES = ['dark', 'sepia', 'light'] as const
  type Theme = (typeof THEMES)[number]
  let fontSize = $state(19)
  let lineHeight = $state(1.7)
  let theme = $state<Theme>('dark')
  const MAX_WIDTH = '65ch' // fixed reading column, not viewport-fill

  const PREFS_KEY = 'mf-prose-reader-prefs'
  function loadPrefs() {
    try {
      const p = JSON.parse(localStorage.getItem(PREFS_KEY) || '{}')
      if (typeof p.fontSize === 'number') fontSize = Math.min(32, Math.max(13, p.fontSize))
      if (typeof p.lineHeight === 'number') lineHeight = Math.min(2.4, Math.max(1.2, p.lineHeight))
      if (p.theme === 'dark' || p.theme === 'sepia' || p.theme === 'light') theme = p.theme
    } catch {
      /* defaults are fine */
    }
  }
  function savePrefs() {
    localStorage.setItem(PREFS_KEY, JSON.stringify({ fontSize, lineHeight, theme }))
  }

  onMount(async () => {
    loadPrefs()
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

  // Navigating to prev/next chapter reuses this route with a new param.
  $effect(() => {
    const id = params.chapterId
    if (manifest && manifest.chapterId !== id) loadChapter(id)
  })

  let resumeFraction = 0
  let resuming = false // true from load until the reader first scrolls themselves
  let applyingResume = false // guards programmatic scrollTop writes from counting as a user scroll
  // Last fraction measured while the DOM was live. flushNow (on unmount/hide) uses THIS rather than
  // re-measuring: during teardown getBoundingClientRect returns zeros, which collapses currentFraction to
  // the last section (~0.978 for a 45-section volume) and would overwrite real progress with near-EOF.
  let lastFraction = 0
  async function loadChapter(id: string) {
    loading = true
    error = ''
    try {
      const [m, c, n] = await Promise.all([getProseManifest(id), getProseContent(id), getNeighbors(id)])
      manifest = m
      content = c
      neighbors = n
      resumeFraction = m.startScrollFraction
      lastFraction = resumeFraction // so exiting without scrolling preserves the resume position
      resuming = true
      // Restore after the new HTML lays out, then keep the position pinned as images decode in (they grow
      // the document and would otherwise push the resume target away) — until the reader takes over.
      requestAnimationFrame(() =>
        requestAnimationFrame(() => {
          applyResume()
          for (const img of articleEl?.querySelectorAll('img') ?? []) {
            if (!(img as HTMLImageElement).complete) img.addEventListener('load', onImageSettled, { once: true })
          }
          setTimeout(() => (resuming = false), 4000)
        }),
      )
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to open chapter.'
    } finally {
      loading = false
    }
  }

  // --- Section-anchored progress (image-load-proof) -------------------------------------------
  let saveTimer: ReturnType<typeof setTimeout> | null = null
  const COMPLETE_AT = 0.985

  function sections(): HTMLElement[] {
    return articleEl ? Array.from(articleEl.querySelectorAll<HTMLElement>('.prose-section')) : []
  }

  // Reading position as a 0..1 fraction over the volume's spine sections: the section at the top of the
  // viewport plus how far we've scrolled into it. Section tops are stable anchors, so this doesn't drift
  // as images elsewhere in the (long, whole-volume) document load in — unlike a raw scrollTop/scrollHeight.
  function currentFraction(): number {
    const secs = sections()
    if (!scrollEl || secs.length === 0) return 0
    const top = scrollEl.getBoundingClientRect().top
    let idx = 0
    for (let i = 0; i < secs.length; i++) {
      if (secs[i].getBoundingClientRect().top - top <= 4) idx = i
      else break
    }
    const sec = secs[idx]
    const into = top - sec.getBoundingClientRect().top
    const intra = sec.offsetHeight > 0 ? Math.min(1, Math.max(0, into / sec.offsetHeight)) : 0
    sectionInfo = { current: idx + 1, total: secs.length }
    return (idx + intra) / secs.length
  }

  function applyResume() {
    const secs = sections()
    if (!scrollEl || secs.length === 0) return
    const pos = Math.min(1, Math.max(0, resumeFraction)) * secs.length
    const idx = Math.min(secs.length - 1, Math.floor(pos))
    const intra = pos - idx
    const sec = secs[idx]
    const top = scrollEl.getBoundingClientRect().top
    applyingResume = true
    scrollEl.scrollTop += sec.getBoundingClientRect().top - top + intra * sec.offsetHeight
    requestAnimationFrame(() => (applyingResume = false))
    lastFraction = currentFraction()
    progressPct = Math.round(lastFraction * 100)
  }

  function onImageSettled() {
    if (resuming) applyResume()
  }

  function onScroll() {
    if (applyingResume) return
    resuming = false
    if (!manifest) return
    const frac = currentFraction()
    lastFraction = frac
    progressPct = Math.round(frac * 100)
    if (saveTimer) clearTimeout(saveTimer)
    const id = manifest.chapterId
    saveTimer = setTimeout(() => {
      saveTimer = null
      saveProseProgress(id, frac, frac >= COMPLETE_AT).catch(() => {})
    }, 700)
  }

  function flushNow() {
    if (saveTimer) {
      clearTimeout(saveTimer)
      saveTimer = null
    }
    if (!manifest) return
    // Uses the cached fraction, NOT a fresh currentFraction() — see lastFraction's note (measuring during
    // teardown reads zeroed rects and lands on ~0.978).
    fetch(`/api/library/chapters/${manifest.chapterId}/prose/progress`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      keepalive: true,
      body: JSON.stringify({ scrollFraction: lastFraction, completed: lastFraction >= COMPLETE_AT }),
    }).catch(() => {})
  }
  function onHide() {
    if (document.visibilityState === 'hidden') flushNow()
  }

  // --- Navigation -----------------------------------------------------------------------------
  function nextChapter() {
    if (manifest) saveProseProgress(manifest.chapterId, 1, true).catch(() => {})
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

  function toggleFullscreen() {
    if (!document.fullscreenElement) readerEl?.requestFullscreen?.().catch(() => {})
    else document.exitFullscreen?.().catch(() => {})
  }
  function onFullscreenChange() {
    isFullscreen = !!document.fullscreenElement
  }

  function bumpFont(delta: number) {
    fontSize = Math.min(32, Math.max(13, fontSize + delta))
    savePrefs()
  }
  function bumpLine(delta: number) {
    lineHeight = Math.min(2.4, Math.max(1.2, Math.round((lineHeight + delta) * 10) / 10))
    savePrefs()
  }
  function cycleTheme() {
    theme = THEMES[(THEMES.indexOf(theme) + 1) % THEMES.length]
    savePrefs()
  }

  function onKey(e: KeyboardEvent) {
    if (showHelp && e.key !== '?' && e.key !== 'Escape') return
    switch (e.key) {
      case 'n': nextChapter(); break
      case 'p': prevChapter(); break
      case '+':
      case '=': bumpFont(1); break
      case '-': bumpFont(-1); break
      case '?': showHelp = !showHelp; break
      case 'Escape': showHelp ? (showHelp = false) : exit(); break
    }
  }

  const label = $derived(
    manifest
      ? [manifest.volume ? `Vol. ${manifest.volume}` : '', manifest.number ? `Ch. ${manifest.number}` : '']
          .filter(Boolean)
          .join(' ') || 'Chapter'
      : '',
  )

  const readingTime = $derived(
    content ? `${Math.max(1, Math.round(content.wordCount / 200))} min read` : '',
  )

  const themeStyle = $derived(
    theme === 'sepia'
      ? 'background:#f4ecd8;color:#5b4636'
      : theme === 'light'
        ? 'background:#ffffff;color:#1a1a1a'
        : 'background:#17171d;color:#d8d8e0',
  )

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
  {:else if manifest && content}
    <header class={barClass('top', !chrome)}>
      <button class={icoClass()} onclick={exit} title="Back to series (Esc)">←</button>
      <div class="flex min-w-0 flex-col leading-[1.1]">
        <a href={seriesHref} class="truncate text-[0.85rem] font-semibold text-foreground no-underline">
          {manifest.seriesTitle}
        </a>
        <span class="text-[0.72rem] text-text-mute">
          {label} · {readingTime}{sectionInfo.total ? ` · ${progressPct}% (§${sectionInfo.current}/${sectionInfo.total})` : ''}
        </span>
      </div>
      <span class="flex-1"></span>
      <button class={icoClass()} onclick={() => bumpFont(-1)} title="Smaller text (-)">A−</button>
      <button class={icoClass()} onclick={() => bumpFont(1)} title="Larger text (+)">A+</button>
      <button class={icoClass()} onclick={() => bumpLine(-0.1)} title="Tighter lines">≡−</button>
      <button class={icoClass()} onclick={() => bumpLine(0.1)} title="Looser lines">≡+</button>
      <button class={icoClass()} onclick={cycleTheme} title="Reading theme">{theme[0].toUpperCase()}</button>
      <button class={icoClass(isFullscreen)} onclick={toggleFullscreen} title="Toggle fullscreen">⛶</button>
      <button class={icoClass()} onclick={() => (showHelp = true)} title="Help (?)">?</button>
    </header>

    <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
    <div
      class="min-h-0 flex-1 overflow-y-auto"
      style={themeStyle}
      bind:this={scrollEl}
      onscroll={onScroll}
      onclick={(e) => {
        // Tap the margins (not selectable text) to toggle chrome.
        if (e.target === e.currentTarget) chrome = !chrome
      }}
    >
      <!-- Server-sanitized HTML (ProseHtmlSanitizer). This is the one remaining client-side XSS surface;
           if server sanitization is ever weakened, that's where to fix it — not with a crutch here. -->
      <article
        class="prose-body mx-auto px-5 py-10"
        bind:this={articleEl}
        style={`max-width:${MAX_WIDTH};font-size:${fontSize}px;line-height:${lineHeight}`}
      >
        {@html content.html}
      </article>
      <div class="mx-auto max-w-[65ch] px-5 pb-16 text-center">
        {#if neighbors.nextChapterId}
          <Button onclick={nextChapter}>Next chapter →</Button>
        {:else}
          <p class="opacity-70">End of the last downloaded chapter.</p>
        {/if}
      </div>
    </div>

    <!-- Always-visible reading-progress bar (stays even when the chrome bars are hidden). -->
    <div class="h-[3px] w-full shrink-0 bg-black/40">
      <div class="h-full bg-brand-soft transition-[width] duration-150" style={`width:${progressPct}%`}></div>
    </div>

    <footer class={barClass('bottom', !chrome)}>
      <button class={icoClass()} onclick={prevChapter} disabled={!neighbors.prevChapterId} title="Previous chapter (p)">⏮</button>
      <span class="flex-1"></span>
      <button class={icoClass()} onclick={nextChapter} disabled={!neighbors.nextChapterId} title="Next chapter (n)">⏭</button>
    </footer>

    {#if showHelp}
      <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
      <div class="absolute inset-0 z-[5] flex items-center justify-center bg-black/60" onclick={() => (showHelp = false)}>
        <div class="max-w-[380px] rounded-[var(--r-lg)] border border-border bg-surface-2 px-6 py-[1.2rem]">
          <h3 class="mb-[0.7rem]">Reader shortcuts</h3>
          <ul class="mb-4 pl-[1.1rem] text-[0.85rem] leading-[1.6] text-text-dim">
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">n</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">p</kbd> — next / previous chapter</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">+</kbd> / <kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">−</kbd> — text size</li>
            <li><kbd class="rounded-[4px] border border-chip-border bg-border px-[0.35rem] py-[0.05rem] text-[0.75rem]">Esc</kbd> — back to series</li>
            <li>Tap the margins to hide chrome</li>
          </ul>
          <Button onclick={() => (showHelp = false)}>Close</Button>
        </div>
      </div>
    {/if}
  {/if}
</div>

<style>
  /* Scoped prose styling for the injected chapter HTML — paragraph rhythm and inline images. */
  .prose-body :global(p) {
    margin: 0 0 1em;
  }
  .prose-body :global(h1),
  .prose-body :global(h2),
  .prose-body :global(h3) {
    line-height: 1.25;
    margin: 1.4em 0 0.6em;
    font-weight: 600;
  }
  .prose-body :global(img) {
    max-width: 100%;
    height: auto;
    display: block;
    margin: 1.2em auto;
  }
  .prose-body :global(hr) {
    border: none;
    border-top: 1px solid currentColor;
    opacity: 0.2;
    margin: 2em 0;
  }
  /* Each spine section is a tracking anchor; give sections after the first a subtle whitespace break. */
  .prose-body :global(.prose-section + .prose-section) {
    margin-top: 3em;
  }
  .prose-body :global(em) {
    font-style: italic;
  }
  .prose-body :global(blockquote) {
    margin: 1em 0;
    padding-left: 1em;
    border-left: 3px solid currentColor;
    opacity: 0.85;
  }
</style>
