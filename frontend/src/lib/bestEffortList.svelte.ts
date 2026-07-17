import { onMount } from 'svelte'

// onMount only requires being called synchronously during component initialization — it doesn't
// need to live directly in the component's own <script> block, just be reached via a synchronous
// call chain from it (see Svelte's onMount doc comment: "doesn't need to live inside the
// component; it can be called from an external module"). Nothing above the onMount() call below
// may be awaited, or lifecycle registration breaks.
export class BestEffortList<T> {
  items = $state<T[]>([])
  constructor(load: () => Promise<T[]>) {
    onMount(async () => {
      try {
        this.items = await load()
      } catch {
        /* best-effort — caller stays empty/hidden */
      }
    })
  }
}
