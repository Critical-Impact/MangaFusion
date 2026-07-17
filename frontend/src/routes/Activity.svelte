<script lang="ts">
  import { onMount } from 'svelte'
  import { link } from 'svelte-spa-router'
  import { getDownloads, getLibraryTitles, type DownloadItem } from '../lib/api'
  import { progressByDownload } from '../lib/signalr.svelte'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  let downloads = $state<DownloadItem[]>([])
  let titles = $state<Record<string, string>>({})
  let loading = $state(true)

  onMount(async () => {
    try {
      const [dl, lib] = await Promise.all([getDownloads(), getLibraryTitles()])
      downloads = dl
      titles = Object.fromEntries(lib.map((s) => [s.id, s.title]))
    } finally {
      loading = false
    }
  })

  const STATUS_COLOR: Record<string, string> = {
    completed: 'text-ok',
    running: 'text-brand-soft',
    failed: 'text-destructive',
    queued: 'text-text-mute',
  }

  // Merge persisted rows with live progress from SignalR.
  function view(d: DownloadItem) {
    const live = progressByDownload[d.id]
    const status = live?.status ?? d.status
    const done = live?.pagesDone ?? d.pagesDone
    const total = live?.pagesTotal ?? d.pagesTotal

    let progress: string
    if (status === 'Queued') progress = 'Queued'
    else if (status === 'Running') progress = total > 0 ? `Downloading ${done}/${total}` : 'Preparing…'
    else if (status === 'Completed') progress = `${total} pages`
    else if (status === 'Failed') progress = d.error ?? 'Failed'
    else progress = status

    return { status, progress, title: titles[d.seriesId] ?? 'Unknown series' }
  }
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-4 text-[1.4rem]">Activity</h1>
  {#if loading}
    <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
  {:else if downloads.length === 0}
    <p class="muted">No downloads yet.</p>
  {:else}
    <ul class="m-0 list-none overflow-hidden rounded-[var(--r-md)] border border-border p-0">
      {#each downloads as d (d.id)}
        {@const v = view(d)}
        <li class="grid grid-cols-[1fr_auto_auto] items-center gap-4 border-b border-border-dim px-4 py-[0.6rem] text-[0.88rem] last:border-b-0">
          <div class="flex min-w-0 flex-col">
            <a href={`/library/${d.seriesId}`} use:link class="overflow-hidden text-ellipsis whitespace-nowrap font-semibold no-underline hover:underline">
              {v.title}
            </a>
            <span class="muted text-[0.78rem]">{d.description ?? ''}</span>
          </div>
          <span class="rounded-full bg-border px-[0.5rem] py-[0.15rem] text-[0.72rem] whitespace-nowrap {STATUS_COLOR[v.status.toLowerCase()] ?? ''}">
            {v.status}
          </span>
          <span
            class={`flex items-center gap-1.5 text-[0.8rem] whitespace-nowrap ${v.status === 'Failed' ? 'max-w-[12rem] overflow-hidden text-ellipsis text-destructive' : 'text-text-mute'}`}
          >
            {#if v.status === 'Running'}<Spinner />{/if}
            {v.progress}
          </span>
        </li>
      {/each}
    </ul>
  {/if}
</section>
