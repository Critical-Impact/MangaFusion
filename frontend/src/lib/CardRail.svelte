<script lang="ts" generics="T">
  import type { Snippet } from 'svelte'
  import { link } from 'svelte-spa-router'

  let {
    title,
    href,
    items,
    key,
    card,
  }: {
    title: string
    /** When set, the rail's title becomes a link (e.g. a collection rail links to its page). */
    href?: string
    items: T[]
    key: (item: T) => string | number
    card: Snippet<[T]>
  } = $props()
</script>

{#if items.length > 0}
  <section class="mb-6">
    <h2 class="mb-[0.7rem] text-base text-foreground">
      {#if href}
        <a {href} use:link class="inline-flex items-center gap-1 text-inherit no-underline hover:text-brand-soft">
          {title}<span aria-hidden="true" class="text-text-mute">›</span>
        </a>
      {:else}
        {title}
      {/if}
    </h2>
    <div class="flex gap-[0.9rem] overflow-x-auto pb-[0.4rem] [scrollbar-width:thin]">
      {#each items as item (key(item))}
        {@render card(item)}
      {/each}
    </div>
  </section>
{/if}
