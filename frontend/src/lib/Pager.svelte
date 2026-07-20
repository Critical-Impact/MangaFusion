<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Button } from './components/ui/button/index.js'

  let {
    page,
    totalPages,
    onprev,
    onnext,
    label,
    total,
    pageSize,
  }: {
    page: number
    totalPages: number
    onprev?: () => void
    onnext?: () => void
    label?: Snippet
    /** Total item count across all pages — when given together with `pageSize`, the default label
     *  shows an item range ("Showing 1–24 of 137") instead of just the page number. */
    total?: number
    pageSize?: number
  } = $props()

  const rangeStart = $derived(pageSize ? page * pageSize + 1 : 0)
  const rangeEnd = $derived(pageSize && total != null ? Math.min((page + 1) * pageSize, total) : 0)
</script>

<div class="mt-6 mb-2 flex items-center justify-center gap-4 text-[0.88rem]">
  <Button variant="secondary" disabled={page <= 0} onclick={() => onprev?.()}>← Prev</Button>
  <span class="text-text-mute">
    {#if label}{@render label()}
    {:else if pageSize && total != null}
      {#if total === 0}No results{:else}Showing {rangeStart}–{rangeEnd} of {total}{/if}
    {:else}
      Page {page + 1} of {totalPages}
    {/if}
  </span>
  <Button variant="secondary" disabled={page >= totalPages - 1} onclick={() => onnext?.()}>Next →</Button>
</div>
