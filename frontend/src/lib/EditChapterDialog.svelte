<script lang="ts">
  import { updateChapter, type LibraryChapter } from './api'
  import { t } from './terms.svelte'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Label } from './components/ui/label/index.js'
  import { Spinner } from './components/ui/spinner/index.js'
  import {
    AlertDialog,
    AlertDialogTrigger,
    AlertDialogContent,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogCancel,
  } from './components/ui/alert-dialog/index.js'

  let {
    chapter,
    allChapters,
    onSaved,
  }: {
    chapter: LibraryChapter
    allChapters: LibraryChapter[]
    onSaved: () => void | Promise<void>
  } = $props()

  let open = $state(false)
  let number = $state('')
  let volume = $state('')
  let title = $state('')
  let saving = $state(false)
  let errorMsg = $state('')

  function reset() {
    number = chapter.number ?? ''
    volume = chapter.volume ?? ''
    title = chapter.title ?? ''
    errorMsg = ''
  }

  function label(c: LibraryChapter): string {
    const parts: string[] = []
    if (c.volume) parts.push(`Vol. ${c.volume}`)
    if (c.number) parts.push(`Ch. ${c.number}`)
    return parts.length ? parts.join(' ') : 'Oneshot'
  }

  // Best-effort preview of where this chapter will sort once saved — mirrors the numeric branch of
  // the server's ChapterNumber.Normalize. Only covers the common numeric case; volume-only and
  // text-key chapters aren't previewed, so a save can still surface a collision this didn't predict.
  const preview = $derived.by(() => {
    const trimmed = number.trim()
    if (trimmed === '') return ''
    const parsed = Number.parseFloat(trimmed)
    if (Number.isNaN(parsed)) return ''

    const siblings = allChapters
      .filter((s) => s.id !== chapter.id && s.language === chapter.language && s.numberSort !== null)
      .sort((a, b) => (a.numberSort as number) - (b.numberSort as number))
    const before = [...siblings].reverse().find((s) => (s.numberSort as number) < parsed)
    const after = siblings.find((s) => (s.numberSort as number) > parsed)

    if (!before && !after) return `Will be the only numbered ${t('chapter')}.`
    if (!before) return `Will sit before ${label(after!)}.`
    if (!after) return `Will sit after ${label(before)}.`
    return `Will sit between ${label(before)} and ${label(after)}.`
  })

  async function save() {
    saving = true
    errorMsg = ''
    try {
      await updateChapter(chapter.id, {
        number: number.trim() || null,
        volume: volume.trim() || null,
        title: title.trim() || null,
      })
      open = false
      await onSaved()
    } catch (e) {
      errorMsg = e instanceof Error ? e.message : `Failed to update ${t('chapter')}.`
    } finally {
      saving = false
    }
  }
</script>

<AlertDialog bind:open>
  <AlertDialogTrigger>
    {#snippet child({ props })}
      <Button
        {...props}
        variant="secondary"
        size="mini"
        onclick={(e: MouseEvent) => {
          reset()
          ;(props.onclick as ((e: MouseEvent) => void) | undefined)?.(e)
        }}
      >
        Edit
      </Button>
    {/snippet}
  </AlertDialogTrigger>
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>Edit {t('chapter')}</AlertDialogTitle>
      <AlertDialogDescription>
        Change this {t('chapter')}'s number, volume, or title. Editing re-sorts it among the
        series' other {t('chapters')} automatically.
      </AlertDialogDescription>
    </AlertDialogHeader>
    <form class="flex flex-col gap-3" onsubmit={(e) => { e.preventDefault(); save() }}>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-number-${chapter.id}`}>Number</Label>
        <Input id={`edit-number-${chapter.id}`} bind:value={number} autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-volume-${chapter.id}`}>Volume</Label>
        <Input id={`edit-volume-${chapter.id}`} bind:value={volume} autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-title-${chapter.id}`}>Title</Label>
        <Input id={`edit-title-${chapter.id}`} bind:value={title} autocomplete="off" />
      </div>
      {#if preview}
        <p class="text-xs text-text-mute">{preview}</p>
      {/if}
      {#if errorMsg}
        <p class="text-xs text-destructive">{errorMsg}</p>
      {/if}
      <AlertDialogFooter>
        <AlertDialogCancel type="button">Cancel</AlertDialogCancel>
        <Button type="submit" disabled={saving}>
          {#if saving}<Spinner />{/if}
          {saving ? 'Saving…' : 'Save'}
        </Button>
      </AlertDialogFooter>
    </form>
  </AlertDialogContent>
</AlertDialog>
