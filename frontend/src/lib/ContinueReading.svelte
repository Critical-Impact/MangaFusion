<script lang="ts">
  import { link } from 'svelte-spa-router'
  import { getContinueReading, dismissReading, type ContinueReadingItem } from './api'
  import Cover from './Cover.svelte'
  import CardRail from './CardRail.svelte'
  import { BestEffortList } from './bestEffortList.svelte'

  const list = new BestEffortList(() => getContinueReading(12))

  function chLabel(i: ContinueReadingItem): string {
    const parts: string[] = []
    if (i.volume) parts.push(`Vol. ${i.volume}`)
    if (i.number) parts.push(`Ch. ${i.number}`)
    return parts.length ? parts.join(' ') : 'Oneshot'
  }
  // A chapter not yet started (pageIndex 0) is the next new/unread one.
  const isNew = (i: ContinueReadingItem) => i.pageIndex <= 0
  function pct(i: ContinueReadingItem): number {
    if (i.pageIndex <= 0 || i.pageCount <= 1) return 0
    return Math.min(100, Math.round(((i.pageIndex + 1) / i.pageCount) * 100))
  }

  async function dismiss(i: ContinueReadingItem) {
    list.items = list.items.filter((x) => x.seriesId !== i.seriesId)
    try {
      await dismissReading(i.seriesId)
    } catch {
      /* best-effort; it'll reappear on next load if this failed */
    }
  }
</script>

<CardRail title="Continue reading" items={list.items} key={(i) => i.chapterId}>
  {#snippet card(i: ContinueReadingItem)}
    <div class="group relative box-border w-40 min-w-0 shrink-0">
      <a
        class="flex flex-col gap-[0.3rem] text-inherit no-underline"
        href={`/read/${i.chapterId}?from=%2F`}
        use:link
        title={`${i.seriesTitle} · ${chLabel(i)}`}
      >
        <Cover src={i.coverUrl} alt="">
          {#snippet overlay()}
            <span
              class={`absolute right-1 bottom-2 rounded-full px-[0.4rem] py-[0.05rem] text-[0.66rem] tabular-nums text-foreground ${isNew(i) ? 'bg-primary/90 font-semibold tracking-[0.02em]' : 'bg-black/70'}`}
            >
              {isNew(i) ? 'NEW' : `${pct(i)}%`}
            </span>
            <span class="absolute inset-x-0 bottom-0 h-1 bg-black/50">
              <span class="block h-full bg-primary" style={`width:${pct(i)}%`}></span>
            </span>
          {/snippet}
        </Cover>
        <span class="max-w-full truncate px-[var(--poster-pad)] text-[0.78rem] text-text-2 group-hover:text-white">
          {i.seriesTitle}
        </span>
        <span class="max-w-full truncate px-[var(--poster-pad)] text-[0.72rem] text-text-mute">
          {chLabel(i)}
        </span>
      </a>
      <a
        class="absolute top-[3px] left-[3px] flex h-[1.15rem] w-[1.15rem] items-center justify-center rounded-full border-0 bg-black/60 text-[0.7rem] leading-none text-foreground opacity-0 transition-opacity duration-[120ms] group-hover:opacity-100 hover:bg-black/80"
        href={`/library/${i.seriesId}`}
        use:link
        title="View series"
      >
        ⓘ
      </a>
      <button
        class="absolute top-[3px] right-[3px] flex h-[1.15rem] w-[1.15rem] items-center justify-center rounded-full border-0 bg-black/60 text-[0.9rem] leading-none text-foreground opacity-0 transition-opacity duration-[120ms] group-hover:opacity-100 hover:bg-[rgba(220,38,127,0.9)]"
        title="Hide from Continue reading"
        onclick={() => dismiss(i)}
      >
        ×
      </button>
    </div>
  {/snippet}
</CardRail>
