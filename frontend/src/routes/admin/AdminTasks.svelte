<script lang="ts">
  import { onMount, onDestroy } from 'svelte'
  import { link } from 'svelte-spa-router'
  import {
    getTasks,
    retryDownloadTask,
    requeueJob,
    deleteJob,
    type TaskFeed,
    type TaskFeedItem,
  } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import FilterBar from '../../lib/FilterBar.svelte'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../../lib/components/ui/select/index.js'
  import { Alert, AlertDescription } from '../../lib/components/ui/alert/index.js'

  const kindFilters = [
    { v: 'all', l: 'All kinds' },
    { v: 'download', l: 'Downloads' },
    { v: 'scan', l: 'Scans' },
  ] as const
  const stateFilters = [
    { v: 'all', l: 'All states' },
    { v: 'active', l: 'Active' },
    { v: 'failed', l: 'Failed' },
    { v: 'done', l: 'Succeeded' },
  ] as const

  let feed = $state<TaskFeed | null>(null)
  let loading = $state(true)
  let error = $state('')
  let kindFilter = $state<'all' | 'download' | 'scan'>('all')
  let stateFilter = $state<'all' | 'active' | 'failed' | 'done'>('all')
  let busy = $state<Record<string, boolean>>({})
  let timer: ReturnType<typeof setInterval> | undefined

  onMount(async () => {
    await refresh()
    loading = false
    timer = setInterval(refresh, 4000) // scans have no live push; poll while open
  })
  onDestroy(() => clearInterval(timer))

  async function refresh() {
    try {
      feed = await getTasks(100)
      error = ''
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to load tasks.'
    }
  }

  const visible = $derived(
    (feed?.items ?? []).filter((i) => {
      if (kindFilter === 'download' && i.kind !== 'download') return false
      if (kindFilter === 'scan' && i.kind === 'download') return false
      if (stateFilter === 'active' && !['Queued', 'Running', 'Scheduled'].includes(i.state)) return false
      if (stateFilter === 'failed' && i.state !== 'Failed') return false
      if (stateFilter === 'done' && i.state !== 'Succeeded') return false
      return true
    }),
  )

  async function act(id: string, fn: () => Promise<unknown>) {
    busy[id] = true
    try {
      await fn()
      await refresh()
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Action failed.')
    } finally {
      busy[id] = false
    }
  }

  function canRetry(i: TaskFeedItem) {
    return i.kind === 'download' && i.state === 'Failed'
  }
  function canManageJob(i: TaskFeedItem) {
    return i.kind !== 'download' && i.hangfireJobId && ['Failed', 'Scheduled', 'Queued'].includes(i.state)
  }

  function kindLabel(k: string) {
    return k === 'download' ? 'Download' : k === 'library-scan' ? 'Library scan' : 'Series scan'
  }
  function when(i: TaskFeedItem) {
    const t = i.finishedAt ?? i.startedAt ?? i.createdAt
    return t ? new Date(t).toLocaleString() : ''
  }
  function progress(i: TaskFeedItem) {
    if (i.kind !== 'download' || i.pagesTotal == null || i.pagesTotal === 0) return ''
    return `${i.pagesDone ?? 0}/${i.pagesTotal}`
  }

  const KIND_COLOR: Record<string, string> = {
    download: 'text-brand-soft',
    'series-scan': 'text-info',
    'library-scan': 'text-info',
  }
  const STATE_COLOR: Record<string, string> = {
    succeeded: 'text-ok',
    running: 'text-brand-soft',
    failed: 'text-destructive',
    queued: 'text-text-mute',
    scheduled: 'text-text-mute',
  }
</script>

{#if loading}
  <p class="muted">Loading…</p>
{:else}
  {#if feed}
    <div class="mb-4 flex flex-wrap items-center gap-4 text-[0.85rem] text-text-dim">
      <span><b class="text-foreground">{feed.stats.processing}</b> running</span>
      <span><b class="text-foreground">{feed.stats.enqueued}</b> queued</span>
      <span><b class="text-foreground">{feed.stats.scheduled}</b> scheduled</span>
      <span><b class="text-ok">{feed.stats.succeeded}</b> succeeded</span>
      <span><b class="text-destructive">{feed.stats.failed}</b> failed</span>
      <span><b class="text-foreground">{feed.stats.servers}</b> server{feed.stats.servers === 1 ? '' : 's'}</span>
      <span class="flex-1"></span>
      <a class="text-[0.8rem] text-brand-soft no-underline hover:underline" href="/hangfire" target="_blank" rel="noreferrer noopener">
        Hangfire dashboard ↗
      </a>
    </div>
  {/if}

  <FilterBar showSearch={false}>
    {#snippet filters()}
      <Select type="single" bind:value={kindFilter}>
        <SelectTrigger>{kindFilters.find((k) => k.v === kindFilter)?.l}</SelectTrigger>
        <SelectContent>
          {#each kindFilters as k (k.v)}<SelectItem value={k.v} label={k.l}>{k.l}</SelectItem>{/each}
        </SelectContent>
      </Select>
      <Select type="single" bind:value={stateFilter}>
        <SelectTrigger>{stateFilters.find((s) => s.v === stateFilter)?.l}</SelectTrigger>
        <SelectContent>
          {#each stateFilters as s (s.v)}<SelectItem value={s.v} label={s.l}>{s.l}</SelectItem>{/each}
        </SelectContent>
      </Select>
    {/snippet}
  </FilterBar>

  {#if error}<Alert variant="destructive" class="mb-4"><AlertDescription>{error}</AlertDescription></Alert>{/if}

  {#if visible.length === 0}
    <p class="muted">No matching tasks.</p>
  {:else}
    <div class="overflow-x-auto rounded-[var(--r-md)] border border-border">
      <table class="w-full border-collapse text-[0.85rem] [&_tr:last-child_td]:border-b-0">
        <thead>
          <tr>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute">Kind</th>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute">Target</th>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute">State</th>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute">Progress</th>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute">When</th>
            <th class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-left text-[0.75rem] font-semibold whitespace-nowrap text-text-mute"></th>
          </tr>
        </thead>
        <tbody>
          {#each visible as i (i.id)}
            <tr>
              <td class="border-b border-border-dim px-[0.75rem] py-[0.5rem] whitespace-nowrap">
                <span class={`rounded-full border border-input bg-surface-3 px-[0.45rem] py-[0.1rem] text-[0.72rem] ${KIND_COLOR[i.kind] ?? ''}`}>
                  {kindLabel(i.kind)}
                </span>
              </td>
              <td class="flex min-w-[14rem] flex-col gap-[0.15rem] border-b border-border-dim px-[0.75rem] py-[0.5rem] whitespace-normal">
                {#if i.seriesId}
                  <a href={`/library/${i.seriesId}`} use:link class="text-text-2 no-underline hover:underline">{i.target}</a>
                {:else}
                  {i.target}
                {/if}
                {#if i.error}
                  <span class="max-w-[28rem] overflow-hidden text-ellipsis text-[0.72rem] text-destructive" title={i.error}>{i.error}</span>
                {/if}
              </td>
              <td class="border-b border-border-dim px-[0.75rem] py-[0.5rem] whitespace-nowrap">
                <span class={`rounded-full bg-border px-[0.45rem] py-[0.1rem] text-[0.72rem] ${STATE_COLOR[i.state.toLowerCase()] ?? ''}`}>
                  {i.state}
                </span>
              </td>
              <td class="border-b border-border-dim px-[0.75rem] py-[0.5rem] whitespace-nowrap tabular-nums">{progress(i)}</td>
              <td class="border-b border-border-dim px-[0.75rem] py-[0.5rem] text-[0.78rem] whitespace-nowrap text-text-mute">{when(i)}</td>
              <td class="border-b border-border-dim px-[0.75rem] py-[0.5rem] whitespace-nowrap">
                <span class="flex justify-end gap-[0.35rem]">
                  {#if canRetry(i)}
                    <Button variant="secondary" size="mini" disabled={busy[i.id]} onclick={() => act(i.id, () => retryDownloadTask(i.id))}>Retry</Button>
                  {/if}
                  {#if canManageJob(i)}
                    <Button variant="secondary" size="mini" disabled={busy[i.id]} onclick={() => act(i.id, () => requeueJob(i.hangfireJobId!))}>Requeue</Button>
                    <Button
                      variant="secondary"
                      size="mini"
                      class="border-danger-border text-destructive"
                      disabled={busy[i.id]}
                      onclick={() => act(i.id, () => deleteJob(i.hangfireJobId!))}
                    >
                      Delete
                    </Button>
                  {/if}
                </span>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
{/if}
