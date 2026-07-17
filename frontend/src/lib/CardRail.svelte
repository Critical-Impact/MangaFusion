<script lang="ts" generics="T">
  import type { Snippet } from 'svelte'

  let {
    title,
    items,
    key,
    card,
  }: {
    title: string
    items: T[]
    key: (item: T) => string | number
    card: Snippet<[T]>
  } = $props()
</script>

{#if items.length > 0}
  <section class="mb-6">
    <h2 class="mb-[0.7rem] text-base text-foreground">{title}</h2>
    <div class="flex gap-[0.9rem] overflow-x-auto pb-[0.4rem] [scrollbar-width:thin]">
      {#each items as item (key(item))}
        {@render card(item)}
      {/each}
    </div>
  </section>
{/if}
