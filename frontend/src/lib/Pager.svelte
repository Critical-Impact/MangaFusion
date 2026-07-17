<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Button } from './components/ui/button/index.js'

  let {
    page,
    totalPages,
    onprev,
    onnext,
    label,
  }: {
    page: number
    totalPages: number
    onprev?: () => void
    onnext?: () => void
    label?: Snippet
  } = $props()
</script>

<div class="mt-6 mb-2 flex items-center justify-center gap-4 text-[0.88rem]">
  <Button variant="secondary" disabled={page <= 0} onclick={() => onprev?.()}>← Prev</Button>
  <span class="text-text-mute">
    {#if label}{@render label()}{:else}Page {page + 1} of {totalPages}{/if}
  </span>
  <Button variant="secondary" disabled={page >= totalPages - 1} onclick={() => onnext?.()}>Next →</Button>
</div>
