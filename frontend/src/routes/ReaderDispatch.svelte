<script lang="ts">
  import { getReaderKind } from '../lib/api'
  import Reader from './Reader.svelte'
  import TextReader from './TextReader.svelte'
  import PdfReader from './PdfReader.svelte'
  import { Spinner } from '../lib/components/ui/spinner/index.js'

  // A chapter reads in the text reader, the image reader, or the PDF reader depending on its artifact's
  // StorageFormat — a per-chapter fact, not a per-library one (a light-novel library can hold all three).
  // Rather than encode that in every link, every /read/:chapterId resolves it here once, then mounts the
  // right reader.
  let { params } = $props<{ params: { chapterId: string } }>()

  let kind = $state<'prose' | 'image' | 'pdf' | null>(null)
  let resolvedFor = $state<string | null>(null)
  // The params actually handed to the mounted reader. We only advance this to a new chapter id *after*
  // its kind resolves, so the currently-mounted reader never sees an id it can't serve — otherwise its
  // own next/prev $effect would fire first and fetch the wrong endpoint (404 → error flash) before we
  // swap components on a format change.
  // Placeholder until the effect resolves the first chapter's kind — nothing renders it while kind is null.
  let readerParams = $state<{ chapterId: string }>({ chapterId: '' })

  // Re-resolve when the chapter id changes (in-reader next/prev navigation reuses this route). Keeps the
  // previously-resolved kind and chapter on screen while re-resolving so the reader isn't torn down for
  // same-format navigation — only a genuine format change swaps components.
  $effect(() => {
    const id = params.chapterId
    if (resolvedFor === id) return
    getReaderKind(id)
      .then((r) => {
        kind = r.kind
        resolvedFor = id
        readerParams = { chapterId: id }
      })
      .catch(() => {
        // Fall back to the image reader (the historical default); a prose chapter would then surface a
        // clear "failed to open" rather than a hard crash.
        kind = 'image'
        resolvedFor = id
        readerParams = { chapterId: id }
      })
  })
</script>

{#if kind === null}
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-bg-reader">
    <p class="flex items-center gap-2 text-text-mute"><Spinner />Loading…</p>
  </div>
{:else if kind === 'prose'}
  <TextReader params={readerParams} />
{:else if kind === 'pdf'}
  <PdfReader params={readerParams} />
{:else}
  <Reader params={readerParams} />
{/if}
