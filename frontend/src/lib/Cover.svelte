<script lang="ts">
  import type { Snippet } from 'svelte'

  // `dim` de-emphasises the artwork (not the overlay, which keeps its badges at full contrast) so a
  // card can read as "already handled" at a glance. Hovering restores it — inside a `group` root, as
  // PosterCard provides — so the full cover is still one pointer away.
  let {
    src,
    alt = '',
    radius = 'var(--r-md)',
    dim = false,
    overlay,
  }: { src?: string | null; alt?: string; radius?: string; dim?: boolean; overlay?: Snippet } = $props()

  const artClass = $derived(
    `h-full w-full transition-[filter,opacity] duration-150 ${dim ? 'opacity-50 grayscale group-hover:opacity-100 group-hover:grayscale-0' : ''}`,
  )
</script>

<div
  class="relative box-border w-[var(--cover-w,100%)] h-[var(--cover-h,auto)] [aspect-ratio:var(--cover-ar,2/3)] rounded-[var(--cover-radius)] overflow-hidden border border-border bg-card"
  style={`--cover-radius:${radius}`}
>
  {#if src}
    <img {src} {alt} loading="lazy" draggable="false" class={`block object-cover ${artClass}`} />
  {:else}
    <div class={`grid place-items-center text-[0.8rem] text-text-faint ${artClass}`}>No cover</div>
  {/if}
  {#if overlay}
    <div class="pointer-events-none absolute inset-0">{@render overlay()}</div>
  {/if}
</div>
