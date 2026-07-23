<script lang="ts">
  import { onDestroy } from 'svelte'
  import { link } from 'svelte-spa-router'
  import {
      startMigrationScan,
      getMigrationBatches,
      getMigrationBatch,
      setMigrationSeriesMatch,
      setMigrationMergeTarget,
      setMigrationItemDisposition,
      commitMigrationSeries,
      commitAllCleanMigrationSeries,
      clearRankingConflicts,
      searchSeries,
      getSeries,
      getLibraryTitles,
      removeMigrationSeries,
      type MigrationBatchSummary,
      type MigrationBatchDetail,
      type MigrationSeriesDetail,
      type MigrationItemDetail,
      type Series, clearMigrationConflict,
  } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import { rankByChapterNumber } from '../../lib/chapterOrder'
  import { progressByMigrationSeries, progressByMigrationBatch } from '../../lib/signalr.svelte'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Input } from '../../lib/components/ui/input/index.js'
  import { Checkbox } from '../../lib/components/ui/checkbox/index.js'
  import { Label } from '../../lib/components/ui/label/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../../lib/components/ui/select/index.js'
  import { Spinner } from '../../lib/components/ui/spinner/index.js'
  import {
      AlertDialog,
      AlertDialogTrigger,
      AlertDialogContent,
      AlertDialogHeader,
      AlertDialogTitle,
      AlertDialogDescription,
      AlertDialogFooter,
      AlertDialogCancel,
      AlertDialogAction,
  } from '../../lib/components/ui/alert-dialog/index.js'

  let batches = $state<MigrationBatchSummary[]>([])
  let batch = $state<MigrationBatchDetail | null>(null)
  let scanning = $state(false)
  let busy = $state<Record<string, boolean>>({})
  let expanded = $state<Record<string, boolean>>({})
  let showCommitted = $state(false)

  // Per-series match search state, keyed by migration series id.
  let matchQuery = $state<Record<string, string>>({})
  let matchResults = $state<Record<string, Series[]>>({})
  let mergeQuery = $state<Record<string, string>>({})
  let libraryTitles = $state<{ id: string; title: string }[]>([])

  // Full MangaDex series detail (for its site URL), keyed by sourceSeriesId — migration always matches
  // against MangaDex, so unlike AdminImport there's no per-batch source to look up.
  let matchedDetail = $state<Record<string, Series>>({})

  let timer: ReturnType<typeof setInterval> | undefined

  const msgOf = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')
  const sizeOf = (b: number) => (b < 1024 ? `${b} B` : b < 1024 * 1024 ? `${(b / 1024).toFixed(0)} KB` : `${(b / 1024 / 1024).toFixed(1)} MB`)

  refresh()

  onDestroy(() => clearInterval(timer))

  async function refresh() {
    try {
      batches = await getMigrationBatches()
      if (!batch && batches.length) await openBatch(batches[0].id)
    } catch (err) {
      notify.error(msgOf(err))
    }
  }

  async function scan() {
    scanning = true
    try {
      const { batchId } = await startMigrationScan()
      await openBatch(batchId)
      watch(batchId)
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      scanning = false
    }
  }

  // The batch sits in 'Scanning' or 'Committing' while a background job runs; both are watched the same way.
  const isBusyStatus = (s: string) => s === 'Scanning' || s === 'Committing'

  async function openBatch(id: string) {
    try {
      batch = await getMigrationBatch(id)
      if (isBusyStatus(batch.status)) watch(id)
    } catch (err) {
      notify.error(msgOf(err))
    }
  }

  function watch(id: string, onDone?: (batch: MigrationBatchDetail) => void) {
    clearInterval(timer)
    timer = setInterval(async () => {
      const updated = await getMigrationBatch(id)
      batch = updated
      if (!isBusyStatus(updated.status)) {
        clearInterval(timer)
        batches = await getMigrationBatches()
        onDone?.(updated)
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

  async function searchMatch(seriesId: string, fallback: string) {
    const q = (matchQuery[seriesId] ?? '').trim() || fallback
    if (!q) return
    try {
      const result = await searchSeries('mangadex', q, { limit: 8, rating: 'all' })
      matchResults[seriesId] = result.items
    } catch (err) {
      notify.error(msgOf(err))
    }
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
      matchedDetail[sourceSeriesId] = await getSeries('mangadex', sourceSeriesId)
    } catch {
      /* "view match" link is best-effort */
    }
  }

  function regimeLabel(r: string) {
    return r === 'Live' ? 'Live' : r === 'Purged' ? 'Purged from MangaDex' : r === 'Mixed' ? 'Partially purged' : 'Unmatched'
  }
  function regimeClass(r: string) {
    return r === 'Live' ? 'text-ok' : r === 'Purged' ? 'text-warn' : r === 'Mixed' ? 'text-warn' : 'text-err-soft'
  }
  // A "clean match" is a NeedsReview series with no conflict and a confident regime — it needs no
  // human input and can go through "Commit all clean matches", so the UI should call it out as such
  // instead of showing the raw "NeedsReview" enum name.
  function isCleanMatch(s: MigrationSeriesDetail) {
    return s.status === 'NeedsReview' && !s.conflictReason && s.regime !== 'Unmatched'
  }
  function seriesStatusLabel(s: MigrationSeriesDetail) {
    if (isCleanMatch(s)) return 'Clean Match'
    return s.status === 'NeedsReview' ? 'Needs Review' : s.status
  }
  function seriesStatusClass(s: MigrationSeriesDetail) {
    if (isCleanMatch(s)) return 'text-ok'
    return statusClass(s.status)
  }
  function statusClass(s: string) {
    return s === 'Committed' ? 'text-ok' : s === 'Failed' ? 'text-err-soft' : s === 'NeedsReview' ? 'text-warn' : ''
  }

  const dispositions = ['Import', 'Duplicate', 'Quarantine']

  // Live preview of commit order — only 'Import'-dispositioned items actually become chapters, so
  // Duplicate/Quarantine items get no order number.
  function orderOf(items: MigrationItemDetail[]): Map<MigrationItemDetail, number> {
    return rankByChapterNumber(
      items.filter((i) => i.disposition === 'Import'),
      (i) => ({ number: i.number, title: i.chapterTitle }),
    )
  }

  // Per-series toggle: view the table in file-scan order (default) or the projected commit order.
  let sortByOrder = $state<Record<string, boolean>>({})

  function displayItems(s: MigrationSeriesDetail, order: Map<MigrationItemDetail, number>): MigrationItemDetail[] {
    if (!sortByOrder[s.id]) return s.items
    return [...s.items].sort((a, b) => (order.get(a) ?? Number.POSITIVE_INFINITY) - (order.get(b) ?? Number.POSITIVE_INFINITY))
  }

  const batchLabel = (b: MigrationBatchSummary) => `${new Date(b.createdAt).toLocaleString()} · ${b.seriesCount} series · ${b.status}`
  const currentBatchLabel = $derived.by(() => {
    const found = batches.find((b) => b.id === batch?.id)
    return found ? batchLabel(found) : ''
  })

  let visibleSeries = $derived(
    batch ? batch.series.filter((s) => showCommitted || s.status !== 'Committed') : [],
  )
  let committedCount = $derived(batch ? batch.series.filter((s) => s.status === 'Committed').length : 0)
  let readyCount = $derived(batch ? batch.series.filter(isCleanMatch).length : 0)
  let rankingOnlyCount = $derived(
    batch ? batch.series.filter((s) => s.status !== 'Committed' && s.hasRankingOnlyConflict).length : 0,
  )

  // Eagerly fetch each matched series' MangaDex detail (for its site URL) as soon as the match is
  // known, so "View match" is ready without the admin having to re-search first.
  $effect(() => {
    for (const s of visibleSeries) {
      if (s.matchedSourceSeriesId) {
        loadMatchedDetail(s.matchedSourceSeriesId)
      }
    }
  })
  // A background commit is in flight for this batch — block starting another one (or a scan) meanwhile.
  let committing = $derived(batch?.status === 'Committing')

  // Prefers the live SignalR push over the series' own persisted progress fields (polling picks
  // those up after a page refresh/reconnect); null when this series has no commit in flight.
  function progressFor(s: MigrationSeriesDetail) {
    const live = progressByMigrationSeries[s.id]
    if (live && live.status === 'Committing') {
      return { itemsDone: live.itemsDone, itemsTotal: live.itemsTotal }
    }
    if (s.commitItemsTotal != null) {
      return { itemsDone: s.commitItemsDone ?? 0, itemsTotal: s.commitItemsTotal }
    }
    return null
  }

  function batchProgress(b: MigrationBatchDetail) {
    const live = progressByMigrationBatch[b.id]
    if (live) return { seriesDone: live.seriesDone, seriesTotal: live.seriesTotal }
    if (b.commitSeriesTotal != null) return { seriesDone: b.commitSeriesDone ?? 0, seriesTotal: b.commitSeriesTotal }
    return null
  }

  async function clearRankingOnly() {
    if (!batch) return
    await act('clear-ranking', async () => {
      const { clearedCount } = await clearRankingConflicts(batch!.id)
      notify.success(clearedCount > 0 ? `Cleared ${clearedCount} ranking-only conflict(s).` : 'No matching conflicts to clear.')
    })
  }

  async function remove(s: MigrationSeriesDetail) {
    await act(s.id + ':remove', async () => {
      await removeMigrationSeries(s.id)
      notify.success(`Removed "${s.matchedTitle ?? s.comicInfoSeriesTitle ?? s.folderName}" from the migrate list.`)
    })
  }

  async function commitAllClean() {
    if (!batch) return
    const id = batch.id
    const before = committedCount
    busy['commit-all'] = true
    try {
      await commitAllCleanMigrationSeries(id)
      // The commit now runs in the background; poll the batch and report the delta when it finishes.
      await openBatch(id)
      watch(id, (done) => {
        const gained = done.series.filter((s) => s.status === 'Committed').length - before
        notify.success(gained > 0 ? `Committed ${gained} series.` : 'Commit finished.')
      })
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      busy['commit-all'] = false
    }
  }
</script>

<div class="flex flex-col gap-4">
  <section class="flex items-center gap-[0.6rem]">
    <Button onclick={scan} disabled={scanning || committing}>
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
    <p class="text-[0.85rem] text-text-mute">No migration batches yet. Drop series folders into the configured inbox, then scan.</p>
  {:else}
    <p class="flex items-center gap-1.5 text-[0.85rem] text-text-mute">
      {#if isBusyStatus(batch.status)}<Spinner />{/if}
      Batch {new Date(batch.createdAt).toLocaleString()} — <strong class={statusClass(batch.status)}>{batch.status}</strong>
      {#if batch.status === 'Scanning'}(matching against MangaDex…){/if}
      {#if batch.status === 'Committing'}(committing in the background…){/if}
      {#if batch.error}<span class="text-err-soft"> — {batch.error}</span>{/if}
    </p>

    {#if batch.divertedFolders.length > 0}
      <p class="m-0 text-[0.85rem] text-warn">
        {batch.divertedFolders.length} folder{batch.divertedFolders.length === 1 ? '' : 's'} had no ComicInfo.xml in
        any file (not from the old MangaDex downloader) and{batch.divertedFolders.length === 1 ? ' was' : ' were'}
        moved to the <a class="underline" href="/admin/import" use:link>Import</a> inbox instead:
        {batch.divertedFolders.join(', ')}
      </p>
    {/if}

    <section class="flex flex-wrap items-center gap-[0.6rem]">
      {#if readyCount > 0}
        <Button variant="secondary" onclick={commitAllClean} disabled={busy['commit-all'] || committing}>
          {#if busy['commit-all'] || committing}<Spinner />{/if}
          {busy['commit-all'] || committing ? 'Committing…' : `Commit all clean matches (${readyCount})`}
        </Button>
      {/if}
      {#if rankingOnlyCount > 0}
        <Button variant="secondary" onclick={clearRankingOnly} disabled={busy['clear-ranking'] || committing}>
          {#if busy['clear-ranking']}<Spinner />{/if}
          {busy['clear-ranking'] ? 'Clearing…' : `Clear ranking-only conflicts (${rankingOnlyCount})`}
        </Button>
      {/if}
    </section>

    {#if committing}
      {@const bp = batchProgress(batch)}
      {#if bp && bp.seriesTotal > 0}
        <div class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
          <span>Committing — series {bp.seriesDone}/{bp.seriesTotal}</span>
          <div class="h-[0.4rem] w-full overflow-hidden rounded-full bg-border">
            <div
              class="h-full bg-brand-soft transition-[width] duration-300"
              style={`width: ${Math.round((bp.seriesDone / bp.seriesTotal) * 100)}%`}
            ></div>
          </div>
        </div>
      {/if}
    {/if}

    {#if batch.series.length === 0 && batch.status !== 'Scanning'}
      <p class="text-[0.85rem] text-text-mute">No series folders found in the inbox.</p>
    {:else if visibleSeries.length === 0}
      <p class="text-[0.85rem] text-text-mute">All series in this batch are committed. Toggle "Show committed" above to see them.</p>
    {/if}

    <ul class="m-0 flex list-none flex-col gap-2 p-0">
      {#each visibleSeries as s (s.id)}
        {@const order = orderOf(s.items)}
        <li class="overflow-hidden rounded-[var(--r-md)] border border-border">
          <button
            class="flex w-full items-center gap-[0.6rem] border-0 bg-[#1c1c24] px-[0.9rem] py-[0.6rem] text-left [font:inherit] text-foreground"
            onclick={() => toggle(s.id)}
          >
            <span class="min-w-[10rem] text-[0.75rem] text-text-mute">{s.folderName}</span>
            <span class="flex-1 font-semibold">{s.matchedTitle ?? s.comicInfoSeriesTitle ?? '—'}</span>
            <span class="rounded-[var(--r-pill)] border border-current px-[0.5rem] py-[0.1rem] text-[0.72rem] {regimeClass(s.regime)}">
              {regimeLabel(s.regime)}
            </span>
            {#if s.regime !== 'Unmatched'}<span class="text-[0.75rem] text-text-mute">{Math.round(s.confidence * 100)}%</span>{/if}
            <span class="rounded-[var(--r-pill)] border border-current px-[0.5rem] py-[0.1rem] text-[0.72rem] {seriesStatusClass(s)}">
              {seriesStatusLabel(s)}
            </span>
          </button>

          {#if expanded[s.id]}
            <div class="flex flex-col gap-[0.7rem] border-t border-border-dim px-[0.9rem] py-[0.8rem]">
              {#if s.conflictReason}<p class="m-0 text-[0.85rem] text-warn">{s.conflictReason}</p>{/if}
              {#if committing}
                {@const p = progressFor(s)}
                {#if p && p.itemsTotal > 0}
                  <div class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
                    <span>Committing — item {p.itemsDone}/{p.itemsTotal}</span>
                    <div class="h-[0.4rem] w-full overflow-hidden rounded-full bg-border">
                      <div
                        class="h-full bg-brand-soft transition-[width] duration-300"
                        style={`width: ${Math.round((p.itemsDone / p.itemsTotal) * 100)}%`}
                      ></div>
                    </div>
                  </div>
                {/if}
              {/if}
              {#if s.committedLibrarySeriesId}
                <a class="text-[0.8rem] text-brand-soft no-underline" href={`/library/${s.committedLibrarySeriesId}`} use:link>
                  Open in library ↗
                </a>
              {/if}

              {#if s.matchedSourceSeriesId && matchedDetail[s.matchedSourceSeriesId]?.siteUrl}
                <a
                  class="text-[0.8rem] text-brand-soft no-underline"
                  href={matchedDetail[s.matchedSourceSeriesId].siteUrl}
                  target="_blank"
                  rel="noreferrer noopener"
                >
                  View match on MangaDex ↗
                </a>
              {/if}

              {#if s.status !== 'Committed'}
                <div class="flex flex-wrap gap-[1.2rem]">
                  <label class="flex min-w-[16rem] flex-1 flex-col gap-[0.3rem] text-[0.78rem] text-text-dim">
                    Match on MangaDex
                    <div class="flex gap-[0.4rem]">
                      <Input
                        placeholder={s.comicInfoSeriesTitle ?? s.folderName}
                        bind:value={matchQuery[s.id]}
                        onkeydown={(e) => e.key === 'Enter' && searchMatch(s.id, s.comicInfoSeriesTitle ?? s.folderName)}
                      />
                      <Button variant="secondary" size="mini" onclick={() => searchMatch(s.id, s.comicInfoSeriesTitle ?? s.folderName)}>
                        Search
                      </Button>
                      {#if s.matchedSourceSeriesId}
                        <Button variant="secondary" size="mini" onclick={() => act(s.id + ':clear', () => setMigrationSeriesMatch(s.id, null))}>
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
                              {#if c.siteUrl }
                                <a target="_blank" href="{c.siteUrl}" class="truncate">{c.title}</a>
                              {:else}
                                <span class="truncate">{c.title}</span>
                              {/if}
                              {#if c.altTitles.length}
                                <span class="truncate text-[0.72rem] text-text-mute">
                                  aka {c.altTitles[0]}{c.altTitles.length > 1 ? ` (+${c.altTitles.length - 1} more)` : ''}
                                </span>
                              {/if}
                            </span>
                            <Button
                              variant="secondary"
                              size="mini"
                              disabled={busy[s.id + ':match']}
                              onclick={() => act(s.id + ':match', () => setMigrationSeriesMatch(s.id, c.sourceSeriesId))}
                            >
                              {s.matchedSourceSeriesId === c.sourceSeriesId ? 'Selected' : 'Select'}
                            </Button>
                          </li>
                        {/each}
                      </ul>
                    {/if}
                  </label>

                  <label class="flex min-w-[16rem] flex-1 flex-col gap-[0.3rem] text-[0.78rem] text-text-dim">
                      {#if s.status !== 'Committed'}
                          <Button
                                  disabled={busy[s.id + ':commit'] || committing}
                                  onclick={() => act(s.id + ':commit', () => commitMigrationSeries(s.id))}
                          >
                              {#if busy[s.id + ':commit'] || committing}<Spinner />{/if}
                              {busy[s.id + ':commit'] || committing ? 'Committing…' : 'Commit this series'}
                          </Button>
                          <Button variant="secondary"
                                  disabled={busy[s.id + ':commit'] || committing || s.matchedSourceSeriesId == null || s.conflictReason == null}
                                  onclick={() => act(s.id + ':commit', () => clearMigrationConflict(s.id))}
                          >
                              {#if busy[s.id + ':commit'] || committing}<Spinner />{/if}
                              {busy[s.id + ':commit'] || committing ? 'Committing…' : 'Clear conflict'}
                          </Button>
                      {/if}
                    Merge into existing library series (optional)
                    <Select
                      type="single"
                      value={s.existingLibrarySeriesId ?? ''}
                      onOpenChange={(open) => { if (open) loadLibraryTitles() }}
                      onValueChange={(v) => act(s.id + ':merge', () => setMigrationMergeTarget(s.id, v || null))}
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
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">File</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">#</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Group</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Pages</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Size</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Disposition</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">Flag</th>
                    <th class="border-b border-border px-[0.4rem] py-[0.3rem] text-left font-medium text-text-mute">
                      <button
                        type="button"
                        class="cursor-pointer border-0 bg-transparent p-0 [font:inherit] text-text-mute underline decoration-dotted hover:text-text-dim"
                        title="Projected chapter order once committed — click to sort the table by it"
                        onclick={() => (sortByOrder[s.id] = !sortByOrder[s.id])}
                      >
                        Order # {sortByOrder[s.id] ? '▼' : ''}
                      </button>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {#each displayItems(s, order) as i (i.id)}
                    <tr>
                      <td class={`max-w-[22rem] border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top [overflow-wrap:anywhere] ${i.isWinner ? 'text-ok' : ''}`}>
                        {i.fileName}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{i.number ?? '—'}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{i.matchedGroup ?? '—'}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{i.pageCount}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">{sizeOf(i.sizeBytes)}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top">
                        {#if s.status === 'Committed'}
                          {i.disposition}
                        {:else}
                          <Select
                            type="single"
                            value={i.disposition}
                            disabled={busy[i.id]}
                            onValueChange={(v) => act(i.id, () => setMigrationItemDisposition(i.id, v))}
                          >
                            <SelectTrigger class="w-32">{i.disposition}</SelectTrigger>
                            <SelectContent>
                              {#each dispositions as d}<SelectItem value={d} label={d}>{d}</SelectItem>{/each}
                            </SelectContent>
                          </Select>
                        {/if}
                      </td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top text-text-mute">{i.flagReason ?? ''}</td>
                      <td class="border-b border-border-dim px-[0.4rem] py-[0.3rem] align-top text-text-mute">{order.get(i) ?? '—'}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>

              {#if s.status !== 'Committed'}
                <Button
                  disabled={busy[s.id + ':commit'] || committing}
                  onclick={() => act(s.id + ':commit', () => commitMigrationSeries(s.id))}
                >
                  {#if busy[s.id + ':commit'] || committing}<Spinner />{/if}
                  {busy[s.id + ':commit'] || committing ? 'Committing…' : 'Commit this series'}
                </Button>
                <Button variant="secondary"
                  disabled={busy[s.id + ':commit'] || committing || s.matchedSourceSeriesId == null || s.conflictReason == null}
                  onclick={() => act(s.id + ':commit', () => clearMigrationConflict(s.id))}
                >
                  {#if busy[s.id + ':commit'] || committing}<Spinner />{/if}
                  {busy[s.id + ':commit'] || committing ? 'Committing…' : 'Clear conflict'}
                </Button>
                <AlertDialog>
                  <AlertDialogTrigger>
                    {#snippet child({ props })}
                      <Button
                        {...props}
                        variant="secondary"
                        class="border-danger-border text-destructive hover:border-destructive"
                        disabled={busy[s.id + ':remove'] || committing}
                      >
                        {#if busy[s.id + ':remove']}<Spinner />{/if}
                        Remove from list
                      </Button>
                    {/snippet}
                  </AlertDialogTrigger>
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Remove "{s.matchedTitle ?? s.comicInfoSeriesTitle ?? s.folderName}"?</AlertDialogTitle>
                      <AlertDialogDescription>
                        Nothing is imported. Its inbox folder is moved to the outbox and this entry drops off the
                        list — a future scan won't pick it up again unless you move the folder back.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction variant="destructive" onclick={() => remove(s)}>Remove</AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
              {/if}
            </div>
          {/if}
        </li>
      {/each}
    </ul>
  {/if}
</div>
