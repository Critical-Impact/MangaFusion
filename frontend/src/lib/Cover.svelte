<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    src,
    alt = '',
    radius = 'var(--r-md)',
    overlay,
  }: { src?: string | null; alt?: string; radius?: string; overlay?: Snippet } = $props()
</script>

<div
  class="relative box-border w-[var(--cover-w,100%)] h-[var(--cover-h,auto)] [aspect-ratio:var(--cover-ar,2/3)] rounded-[var(--cover-radius)] overflow-hidden border border-border bg-card"
  style={`--cover-radius:${radius}`}
>
  {#if src}
    <img {src} {alt} loading="lazy" draggable="false" class="block h-full w-full object-cover" />
  {:else}
    <div class="grid h-full place-items-center text-[0.8rem] text-text-faint">No cover</div>
  {/if}
  {#if overlay}
    <div class="pointer-events-none absolute inset-0">{@render overlay()}</div>
  {/if}
</div>
