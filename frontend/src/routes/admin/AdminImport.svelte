<script lang="ts">
  import { onDestroy } from 'svelte'
  import { link } from 'svelte-spa-router'
  import {
    startImportScan,
    getImportBatches,
    getImportBatch,
    setImportSeriesMatch,
    setImportMergeTarget,
    setImportTitleOverride,
    setImportItem,
    commitImportSeries,
    searchImportCandidates,
    getSeries,
    getLibraryTitles,
    type ImportBatchSummary,
    type ImportBatchDetail,
    type ImportSeriesDetail,
    type ImportItemDetail,
    type ImportCandidate,
    type Series,
  } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Input } from '../../lib/components/ui/input/index.js'
  import { Checkbox } from '../../lib/components/ui/checkbox/index.js'
  import { Label } from '../../lib/components/ui/label/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../../lib/components/ui/select/index.js'
  import { Spinner } from '../../lib/components/ui/spinner/index.js'
  import Cover from '../../lib/Cover.svelte'
  import InfoIcon from '@lucide/svelte/icons/info'
  import { progressByImportSeries } from '../../lib/signalr.svelte'

  let batches = $state<ImportBatchSummary[]>([])
  let batch = $state<ImportBatchDetail | null>(null)
  let scanning = $state(false)
  let busy = $state<Record<string, boolean>>({})
  let expanded = $state<Record<string, boolean>>({})
  let showCommitted = $state(false)

  // Per-series match search state, keyed by import series id.
  let matchQuery = $state<Record<string, string>>({})
  let matchResults = $state<Record<string, ImportCandidate[]>>({})
  let libraryTitles = $state<{ id: string; title: string }[]>([])

  // Full MangaUpdates series detail (for its alt-titles), keyed by sourceSeriesId, loaded lazily the
  // first time the title-override picker for a matched series is opened.
  let matchedDetail = $state<Record<string, Series>>({})

  // Per-item editable chapter-spec draft, keyed by import item id — mirrors AdminLocal's per-file
  // `imp` draft: freely editable, only sent on explicit save.
  let itemDraft = $state<Record<string, { number: string; volume: string; title: string }>>({})

  let timer: ReturnType<typeof setInterval> | undefined

  const msgOf = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')
  const sizeOf = (b: number) =>
    b < 1024 ? `${b} B` : b < 1024 * 1024 ? `${(b / 1024).toFixed(0)} KB` : `${(b / 1024 / 1024).toFixed(1)} MB`

  refresh()

  onDestroy(() => clearInterval(timer))

  function syncDrafts() {
    if (!batch) return
    for (const s of batch.series) {
      for (const i of s.items) {
        itemDraft[i.id] ??= { number: i.number ?? '', volume: i.volume ?? '', title: i.title ?? '' }
      }
    }
  }

  async function refresh() {
    try {
      batches = await getImportBatches()
      if (!batch && batches.length) await openBatch(batches[0].id)
    } catch (err) {
      notify.error(msgOf(err))
    }
  }

  async function scan() {
    scanning = true
    try {
      const { batchId } = await startImportScan()
      await openBatch(batchId)
      watch(batchId)
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      scanning = false
    }
  }

  // Keep polling while the batch is scanning OR any series in it is actively committing — the live
  // SignalR push is the primary feedback channel during a commit, but this polling is the durable
  // fallback (a page reload picks progress back up, a dropped SignalR connection isn't silent forever)
  // and is what eventually reflects the terminal Committed/NeedsReview(+error) state either way.
  function hasActivity(b: ImportBatchDetail | null): boolean {
    return !!b && (b.status === 'Scanning' || b.series.some((s) => s.status === 'Committing'))
  }

  async function openBatch(id: string) {
    try {
      batch = await getImportBatch(id)
      syncDrafts()
      if (hasActivity(batch)) watch(id)
    } catch (err) {
      notify.error(msgOf(err))
    }
  }

  function watch(id: string) {
    clearInterval(timer)
    timer = setInterval(async () => {
      const updated = await getImportBatch(id)
      batch = updated
      syncDrafts()
      if (!hasActivity(updated)) {
        clearInterval(timer)
        batches = await getImportBatches()
      }
    }, 2500)
  }

  function toggle(seriesId: string) {
    expanded[seriesId] = !expanded[seriesId]
  }

  async function act(key: string, fn: () => Promise<unknown>) {
    busy[key] = true
    try {
      await fn()
      if (batch) await openBatch(batch.id)
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      busy[key] = false
    }
  }

  // Always the *batch's* match source, never a hardcoded one: a comic batch is matched against ComicVine
  // and a manga batch against MangaUpdates, and searching the wrong one returns confidently wrong results.
  const matchSourceId = $derived(batch?.matchSourceId ?? 'mangaupdates')
  const matchSourceName = $derived(
    matchSourceId === 'comicvine' ? 'ComicVine' : matchSourceId === 'mangaupdates' ? 'MangaUpdates' : matchSourceId,
  )
  const isComicBatch = $derived(batch?.kind === 'Comic')

  // Goes through the import wizard's own candidate route, not the generic source search: only it knows the
  // batch's source *and* how many files this series has, which is what sinks a 1-issue volume when you're
  // importing 20 files.
  async function searchMatch(seriesId: string, fallback: string) {
    const q = (matchQuery[seriesId] ?? '').trim() || fallback
    if (!q) return
    try {
      matchResults[seriesId] = await searchImportCandidates(seriesId, q)
    } catch (err) {
      notify.error(msgOf(err))
    }
  }

  async function selectMatch(series: ImportSeriesDetail, sourceSeriesId: string) {
    await act(series.id + ':match', () => setImportSeriesMatch(series.id, sourceSeriesId))
    // Selection made — the candidate list has done its job; collapse it back out of the way.
    matchResults[series.id] = []
  }

  async function loadLibraryTitles() {
    if (libraryTitles.length) return
    try {
      libraryTitles = await getLibraryTitles()
    } catch {
      /* merge-target picker is best-effort */
    }
  }

  async function loadMatchedDetail(sourceSeriesId: string) {
    if (matchedDetail[sourceSeriesId]) return
    try {
      matchedDetail[sourceSeriesId] = await getSeries(matchSourceId, sourceSeriesId)
    } catch {
      /* alt-title picker is best-effort — falls back to just the primary title */
    }
  }

  function setTitle(seriesId: string, title: string | null) {
    return act(seriesId + ':title', () => setImportTitleOverride(seriesId, title))
  }

  function toggleInclude(item: ImportItemDetail) {
    return act(item.id + ':include', () =>
      setImportItem(item.id, !item.include, item.number, item.volume, item.title),
    )
  }

  // An item an earlier (failed) commit already imported stays visible but frozen: its chapter is in the
  // library and its source file is gone, so there is nothing an edit could still change — and the server
  // rejects one anyway. Note this is not the same as "the series isn't in review": a partially-committed
  // series is back in NeedsReview precisely so its *remaining* items can still be edited and retried.
  function isEditable(series: ImportSeriesDetail, item: ImportItemDetail) {
    return series.status === 'NeedsReview' && !item.imported
  }

  // Deliberately not routed through act(): that helper refetches the whole batch on success, which
  // briefly disables every field in the row (all three shared one busy key) right as the user clicks
  // into the next one — the field wouldn't accept the click until the refetch finished, so it took two
  // clicks to move between fields. Updating the item in place avoids both the refetch and the shared
  // busy key.
  async function saveItem(item: ImportItemDetail) {
    const d = itemDraft[item.id]
    const number = d.number.trim() || null
    const volume = d.volume.trim() || null
    const title = d.title.trim() || null
    const key = item.id + ':fields'
    busy[key] = true
    try {
      await setImportItem(item.id, item.include, number, volume, title)
      item.number = number
      item.volume = volume
      item.title = title
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      busy[key] = false
    }
  }

  async function commitSeries(series: ImportSeriesDetail) {
    await act(series.id + ':commit', () => commitImportSeries(series.id))
    // The commit itself now just enqueues a background job and returns — make sure polling is
    // running so we eventually see it finish even if the SignalR push is missed/disconnected.
    if (batch) watch(batch.id)
  }

  // Prefers the live SignalR push (smooth, page-by-page); falls back to the batch's own persisted
  // progress fields (from polling) if no live message has arrived yet for this series.
  function progressFor(s: ImportSeriesDetail) {
    const live = progressByImportSeries[s.id]
    if (live && live.status === 'Committing') {
      return { itemsDone: live.itemsDone, itemsTotal: live.itemsTotal, pageDone: live.pageDone, pageTotal: live.pageTotal }
    }
    return {
      itemsDone: s.commitItemsDone ?? 0,
      itemsTotal: s.commitItemsTotal ?? 0,
      pageDone: s.commitPageDone,
      pageTotal: s.commitPageTotal,
    }
  }

  function statusClass(s: string) {
    return s === 'Committed' ? 'text-ok'
      : s === 'Failed' ? 'text-err-soft'
      : s === 'Committing' ? 'text-brand-soft'
      : s === 'NeedsReview' ? 'text-warn'
      : ''
  }

  const batchLabel = (b: ImportBatchSummary) => `${new Date(b.createdAt).toLocaleString()} · ${b.seriesCount} series · ${b.status}`
  const currentBatchLabel = $derived.by(() => {
    const found = batches.find((b) => b.id === batch?.id)
    return found ? batchLabel(found) : ''
  })

  let visibleSeries = $derived(
    batch ? batch.series.filter((s) => showCommitted || s.status !== 'Committed') : [],
  )
  let committedCount = $derived(batch ? batch.series.filter((s) => s.status === 'Committed').length : 0)

  // Eagerly (not lazily-on-dropdown-open) fetch alt-titles for every matched series as soon as it's
  // known — including a match the scan pre-filled automatically — so "Title to use" already has its
  // alternatives ready the first time it's opened, rather than only after a manual re-search/select.
  $effect(() => {
    for (const s of visibleSeries) {
      if (s.matchedSourceSeriesId) {
        loadMatchedDetail(s.matchedSourceSeriesId)
      }
    }
  })
  const includedCount = (items: ImportItemDetail[]) => items.filter((i) => i.include).length

  // Mirrors the server's ChapterNumber.Normalize key just closely enough to warn before commit, not to
  // be the source of truth (the backend re-validates properly, including against numbers already used
  // elsewhere in the target series). A blank number with a volume set means "this file is the whole
  // volume" — it keys off the volume instead of collapsing every blank-number item to one "oneshot".
  const numberKey = (n: string | null, v: string | null) => {
    const num = n?.trim()
    if (num) return num.toLowerCase()
    const vol = v?.trim()
    return vol ? `vol-${vol.toLowerCase()}` : 'oneshot'
  }
  const hasDuplicateNumbers = (items: ImportItemDetail[]) => {
    const seen = new Set<string>()
    for (const i of items.filter((i) => i.include)) {
      const key = numberKey(i.number, i.volume)
      if (seen.has(key)) return true
      seen.add(key)
    }
    return false
  }
</script>

<div class="flex flex-col gap-4">
  <p class="text-[0.85rem] text-text-mute">
    Scans this library's import inbox for manually-sourced release folders (e.g. purchased digital volumes),
    suggests a metadata match for each, and imports them as chapters once you confirm (or correct) the match.
    PDF and CBZ files are supported, one release per subfolder.
  </p>

  <section class="flex items-center gap-[0.6rem]">
    <Button onclick={scan} disabled={scanning}>
      {#if scanning}<Spinner />{/if}
      {scanning ? 'Starting…' : 'Scan inbox'}
    </Button>
    {#if batches.length > 1}
      <Select type="single" value={batch?.id ?? ''} onValueChange={(v) => openBatch(v)}>
        <SelectTrigger>{currentBatchLabel}</SelectTrigger>
        <SelectContent>
          {#each batches as b (b.id)}<SelectItem value={b.id} label={batchLabel(b)}>{batchLabel(b)}</SelectItem>{/each}
        </SelectContent>
      </Select>
    {/if}
    {#if committedCount > 0}
      <div class="flex items-center gap-[0.4rem] text-[0.8rem] text-text-dim">
        <Checkbox id="show-committed" bind:checked={showCommitted} />
        <Label for="show-committed" class="cursor-pointer">Show committed ({committedCount})</Label>
      </div>
    {/if}
  </section>

  {#if !batch}
    <p class="text-[0.85rem] text-text-mute">No import batches yet. Drop release folders into the configured import inbox, then scan.</p>
  {:else}
    <p class="flex items-center gap-1.5 text-[0.85rem] text-text-mute">
      {#if batch.status === 'Scanning'}<Spinner />{/if}
      Batch {new Date(batch.createdAt).toLocaleString()} — <strong class={statusClass(batch.status)}>{batch.status}</strong>
      {#if batch.status === 'Scanning'}(matching against {matchSourceName}…){/if}
      {#if batch.error}<span class="text-err-soft"> — {batch.error}</span>{/if}
    </p>

    {#if batch.series.length === 0 && batch.status !== 'Scanning'}
      <p class="text-[0.85rem] text-text-mute">No release folders found in the import inbox.</p>
    {:else if visibleSeries.length === 0}
      <p class="text-[0.85rem] text-text-mute">All series in this batch are committed. Toggle "Show committed" above to see them.</p>
    {/if}

    <ul class="m-0 flex list-none flex-col gap-2 p-0">
      {#each visibleSeries as s (s.id)}
        <li class="overflow-hidden rounded-[var(--r-md)] border border-border">
          <button
            class="flex w-full items-center gap-[0.6rem] border-0 bg-[#1c1c24] px-[0.9rem] py-[0.6rem] text-left [font:inherit] text-foreground"
            onclick={() => toggle(s.id)}
          >
            <span class="min-w-[10rem] text-[0.75rem] text-text-mute">{s.groupTitle}</span>
            <span class="flex-1 font-semibold">{s.titleOverride ?? s.matchedTitle ?? '— no match —'}</span>
            <span class="text-[0.75rem] text-text-mute">{includedCount(s.items)}/{s.items.length} included</span>
            <span class="rounded-[var(--r-pill)] border border-current px-[0.5rem] py-[0.1rem] text-[0.72rem] {statusClass(s.status)}">
              {s.status}
            </span>
          </button>

          {#if expanded[s.id]}
            <div class="flex flex-col gap-[0.7rem] border-t border-border-dim px-[0.9rem] py-[0.8rem]">
              {#if s.committedLibrarySeriesId}
                <a class="text-[0.8rem] text-brand-soft no-underline" href={`/library/${s.committedLibrarySeriesId}`} use:link>
                  Open in library ↗
                </a>
              {/if}

              {#if s.commitError}
                <p class="m-0 text-[0.8rem] text-err-soft">Last commit attempt failed: {s.commitError}</p>
              {/if}

              {#if s.status === 'Committing'}
                {@const p = progressFor(s)}
                <div class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
                  <span>
                    Committing — item {p.itemsDone}/{p.itemsTotal}
                    {#if p.pageTotal}· converting page {p.pageDone ?? 0}/{p.pageTotal}{/if}
                  </span>
                  <div class="h-[0.4rem] w-full overflow-hidden rounded-full bg-border">
                    <div
                      class="h-full bg-brand-soft transition-[width] duration-300"
                      style={`width: ${
                        p.pageTotal
                          ? Math.round(((p.pageDone ?? 0) / p.pageTotal) * 100)
                          : p.itemsTotal
                            ? Math.round((p.itemsDone / p.itemsTotal) * 100)
                            : 0
                      }%`}
                    ></div>
                  </div>
                </div>
              {/if}

              {#if s.status === 'NeedsReview'}
                <div class="flex flex-wrap gap-[1.2rem]">
                  <label class="flex min-w-[16rem] flex-1 flex-col gap-[0.3rem] text-[0.78rem] text-text-dim">
                    Match on {matchSourceName}
                    <div class="flex gap-[0.4rem]">
                      <Input
                        placeholder={s.groupTitle}
                        bind:value={matchQuery[s.id]}
                        onkeydown={(e) => e.key === 'Enter' && searchMatch(s.id, s.groupTitle)}
                      />
                      <Button variant="secondary" size="mini" onclick={() => searchMatch(s.id, s.groupTitle)}>
                        Search
                      </Button>
                      {#if s.matchedSourceSeriesId}
                        <Button variant="secondary" size="mini" onclick={() => act(s.id + ':clear', () => setImportSeriesMatch(s.id, null))}>
                          Clear match
                        </Button>
                      {/if}
                    </div>
                    {#if matchResults[s.id]?.length}
                      <ul class="m-0 mt-[0.3rem] flex list-none flex-col gap-[0.2rem] p-0">
                        {#each matchResults[s.id] as c (c.sourceSeriesId)}
                          <li class="flex items-center justify-between gap-2 text-[0.8rem]">
                            <span
                              class="flex min-w-0 cursor-default flex-col"
                              title={c.altTitles.length ? `Also known as:\n${c.altTitles.join('\n')}` : undefined}
                            >
                              <span class="flex min-w-0 items-baseline gap-[0.4rem]">
                                <span class="truncate">{c.title}</span>
                                {#if c.year}<span class="shrink-0 text-text-mute">({c.year})</span>{/if}
                              </span>
                              <span class="flex flex-wrap items-baseline gap-x-[0.5rem] text-[0.72rem] text-text-mute">
                                {#if c.chapterCount != null}
                                  <!-- The count is the thing that separates identically-titled relaunches, so
                                       it's called out rather than buried — and flagged when it's too small to
                                       hold what's being imported. -->
                                  <span class={c.chapterCount < s.items.length ? 'text-warn' : ''}>
                                    {c.chapterCount} issue{c.chapterCount === 1 ? '' : 's'}
                                    {#if c.chapterCount < s.items.length}· fewer than your {s.items.length} file{s.items.length === 1 ? '' : 's'}{/if}
                                  </span>
                                {/if}
                                {#if c.siteUrl}
                                  <a
                                    href={c.siteUrl}
                                    target="_blank"
                                    rel="noreferrer noopener"
                                    class="underline hover:text-text-dim"
                                  >
                                    View on {matchSourceName}
                                  </a>
                                {/if}
                                {#if c.altTitles.length}
                                  <span class="truncate">
                                    aka {c.altTitles[0]}{c.altTitles.length > 1 ? ` (+${c.altTitles.length - 1} more)` : ''}
                                  </span>
                                {/if}
                              </span>
                            </span>
                            <span class="group/cover relative shrink-0">
                              <InfoIcon class="size-[0.95rem] cursor-help text-text-mute hover:text-text-dim" />
                              <div
                                class="pointer-events-none absolute right-0 bottom-full z-10 mb-[0.3rem] hidden w-[6.5rem] group-hover/cover:block"
                              >
                                <Cover src={c.coverUrl} alt={c.title} />
                              </div>
                            </span>
                            <Button
                              variant="secondary"
                              size="mini"
                              disabled={busy[s.id + ':match']}
                              onclick={() => selectMatch(s, c.sourceSeriesId)}
                            >
                              {s.matchedSourceSeriesId === c.sourceSeriesId ? 'Selected' : 'Select'}
                            </Button>
                          </li>
                        {/each}
                      </ul>
                    {/if}

                    {#if s.matchedSourceSeriesId && !s.existingLibrarySeriesId}
                      <div class="mt-[0.3rem] flex flex-col gap-[0.3rem]">
                        <span class="text-[0.78rem] text-text-dim">Title to use</span>
                        <Select
                          type="single"
                          value={s.titleOverride ?? s.matchedTitle ?? ''}
                          onValueChange={(v) => setTitle(s.id, v === (s.matchedTitle ?? '') ? null : v)}
                        >
                          <SelectTrigger>{s.titleOverride ?? s.matchedTitle}</SelectTrigger>
                          <SelectContent>
                            <SelectItem value={s.matchedTitle ?? ''} label={`${s.matchedTitle} (primary)`}>
                              {s.matchedTitle} (primary)
                            </SelectItem>
                            {#each matchedDetail[s.matchedSourceSeriesId]?.altTitles ?? [] as alt (alt)}
                              <SelectItem value={alt} label={alt}>{alt}</SelectItem>
                            {/each}
                          </SelectContent>
                        </Select>
                      </div>
                    {/if}
                  </label>

                  <label class="flex min-w-[16rem] flex-1 flex-col gap-[0.3rem] text-[0.78rem] text-text-dim">
                    Merge into existing library series (optional)
                    <Select
                      type="single"
                      value={s.existingLibrarySeriesId ?? ''}
                      onOpenChange={(open) => { if (open) loadLibraryTitles() }}
                      onValueChange={(v) => act(s.id + ':merge', () => setImportMergeTarget(s.id, v || null))}
                    >
                      <SelectTrigger>
                        {libraryTitles.find((t) => t.id === s.existingLibrarySeriesId)?.title ?? '— create new series —'}
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="" label="— create new series —">— create new series —</SelectItem>
                        {#each libraryTitles as t (t.id)}<SelectItem value={t.id} label={t.title}>{t.title}</SelectItem>{/each}
                      </SelectContent>
                    </Select>
                  </label>
                </div>
              {/if}

              <table class="w-full border-collapse text-[0.78rem]">
                <thead>
                  <tr>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Include</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">File</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Format</th>
                    <!-- Comics ship one file per issue, so the number is pre-filled from the filename
                         ("100 Bullets #017" → 17). Manga releases are one file per volume, where a blank
                         number means "import the whole volume as one artifact". -->
                    <th
                      class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute"
                      title={isComicBatch
                        ? 'The issue number, read from the filename. Correct it if the parse guessed wrong.'
                        : 'Leave blank to import this file as the entire volume, not a numbered chapter.'}
                    >{isComicBatch ? 'Issue' : 'Number'}</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Volume</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Title</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Pages</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Size</th>
                  </tr>
                </thead>
                <tbody>
                  {#each s.items as i (i.id)}
                    <tr class={i.include ? '' : 'opacity-50'}>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">
                        <Checkbox
                          checked={i.include}
                          disabled={busy[i.id + ':include'] || !isEditable(s, i)}
                          onCheckedChange={() => toggleInclude(i)}
                        />
                      </td>
                      <td class="max-w-[16rem] border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top [overflow-wrap:anywhere]">
                        {i.fileName}
                        {#if i.imported}
                          <span class="ml-[0.35rem] whitespace-nowrap rounded-[var(--r-pill)] border border-current px-[0.35rem] py-[0.05rem] text-[0.68rem] text-ok">
                            imported
                          </span>
                        {/if}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{i.format}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">
                        {#if !isEditable(s, i)}
                          {i.number ?? '—'}
                        {:else if itemDraft[i.id]}
                          <Input
                            class="w-[4.5rem]"
                            placeholder="whole vol."
                            bind:value={itemDraft[i.id].number}
                            disabled={busy[i.id + ':fields']}
                            onblur={() => saveItem(i)}
                          />
                        {/if}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">
                        {#if !isEditable(s, i)}
                          {i.volume ?? '—'}
                        {:else if itemDraft[i.id]}
                          <Input
                            class="w-[4.5rem]"
                            bind:value={itemDraft[i.id].volume}
                            disabled={busy[i.id + ':fields']}
                            onblur={() => saveItem(i)}
                          />
                        {/if}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">
                        {#if !isEditable(s, i)}
                          {i.title ?? '—'}
                        {:else if itemDraft[i.id]}
                          <Input
                            class="w-[8rem]"
                            bind:value={itemDraft[i.id].title}
                            disabled={busy[i.id + ':fields']}
                            onblur={() => saveItem(i)}
                          />
                        {/if}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{i.pageCount}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{sizeOf(i.sizeBytes)}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>

              {#if s.status === 'NeedsReview'}
                {#if hasDuplicateNumbers(s.items)}
                  <p class="m-0 text-[0.8rem] text-err-soft">
                    Two or more included items share the same chapter number — or the same volume, if you've left
                    the number blank to import a whole volume — give each a distinct number (or volume) before
                    committing.
                  </p>
                {/if}
                <Button
                  disabled={busy[s.id + ':commit'] || includedCount(s.items) === 0 || hasDuplicateNumbers(s.items)}
                  onclick={() => commitSeries(s)}
                >
                  {#if busy[s.id + ':commit']}<Spinner />{/if}
                  {busy[s.id + ':commit'] ? 'Starting…' : 'Commit this series'}
                </Button>
              {/if}
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>
