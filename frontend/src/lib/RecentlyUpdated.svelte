<script lang="ts">
  import { t } from './terms.svelte'
  import { link } from 'svelte-spa-router'
  import { getRecentlyUpdated, type RecentlyUpdatedItem } from './api'
  import Cover from './Cover.svelte'
  import CardRail from './CardRail.svelte'
  import { BestEffortList } from './bestEffortList.svelte'

  const list = new BestEffortList(() => getRecentlyUpdated(12))

  function chLabel(i: RecentlyUpdatedItem): string {
    const parts: string[] = []
    if (i.volume) parts.push(`Vol. ${i.volume}`)
    if (i.number) parts.push(`Ch. ${i.number}`)
    return parts.length ? parts.join(' ') : `New ${t('chapter')}`
  }

  function href(i: RecentlyUpdatedItem): string {
    return i.chapterId ? `/read/${i.chapterId}?from=%2F` : `/library/${i.seriesId}`
  }
</script>

<CardRail title="Recently updated" items={list.items} key={(i) => i.seriesId}>
  {#snippet card(i: RecentlyUpdatedItem)}
    <div class="group relative w-40 min-w-0 shrink-0">
      <a
        class="flex flex-col gap-[0.3rem] text-inherit no-underline"
        href={href(i)}
        use:link
        title={`${i.seriesTitle} · ${chLabel(i)}`}
      >
        <Cover src={i.coverUrl} alt="" />
        <span class="max-w-full truncate px-[var(--poster-pad)] text-[0.78rem] text-text-2 group-hover:text-white">
          {i.seriesTitle}
        </span>
        <span class="max-w-full truncate px-[var(--poster-pad)] text-[0.72rem] text-text-mute">
          {chLabel(i)} · {new Date(i.updatedAt).toLocaleDateString()}
        </span>
      </a>
      {#if i.chapterId}
        <a
          class="absolute top-[3px] left-[3px] flex h-[1.15rem] w-[1.15rem] items-center justify-center rounded-full border-0 bg-black/60 text-[0.7rem] leading-none text-foreground opacity-0 transition-opacity duration-[120ms] group-hover:opacity-100 hover:bg-black/80"
          href={`/library/${i.seriesId}`}
          use:link
          title="View series"
        >
          ⓘ
        </a>
      {/if}
    </div>
  {/snippet}
</CardRail>
