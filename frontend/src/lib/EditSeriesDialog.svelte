<script lang="ts">
  import { updateSeriesMetadata, unlockSeriesMetadata, type LibrarySeriesDetail } from './api'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Label } from './components/ui/label/index.js'
  import { Textarea } from './components/ui/textarea/index.js'
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
    series,
    onSaved,
  }: {
    series: LibrarySeriesDetail
    onSaved: () => void | Promise<void>
  } = $props()

  let open = $state(false)
  let title = $state('')
  let year = $state('')
  let description = $state('')
  let saving = $state(false)
  let unlocking = $state(false)
  let errorMsg = $state('')

  const locked = $derived(series.titleLocked || series.yearLocked || series.descriptionLocked)

  function reset() {
    title = series.title
    year = series.year != null ? String(series.year) : ''
    description = series.description ?? ''
    errorMsg = ''
  }

  async function save() {
    if (!title.trim()) {
      errorMsg = 'Title is required.'
      return
    }
    saving = true
    errorMsg = ''
    try {
      const parsedYear = year.trim() ? Number.parseInt(year.trim(), 10) : null
      await updateSeriesMetadata(series.id, {
        title: title.trim(),
        year: parsedYear !== null && Number.isNaN(parsedYear) ? null : parsedYear,
        description: description.trim() || null,
      })
      open = false
      await onSaved()
    } catch (e) {
      errorMsg = e instanceof Error ? e.message : 'Failed to update series.'
    } finally {
      saving = false
    }
  }

  async function unlock() {
    unlocking = true
    try {
      await unlockSeriesMetadata(series.id)
      await onSaved()
    } catch (e) {
      errorMsg = e instanceof Error ? e.message : 'Failed to unlock metadata.'
    } finally {
      unlocking = false
    }
  }
</script>

<AlertDialog bind:open>
  <AlertDialogTrigger>
    {#snippet child({ props })}
      <Button
        {...props}
        variant="secondary"
        size="sm"
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
      <AlertDialogTitle>Edit series</AlertDialogTitle>
      <AlertDialogDescription>
        Change this series' title, year, or description. Editing locks these fields so a metadata
        refresh or scan won't overwrite them again.
      </AlertDialogDescription>
    </AlertDialogHeader>
    <form class="flex flex-col gap-3" onsubmit={(e) => { e.preventDefault(); save() }}>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-series-title-${series.id}`}>Title</Label>
        <Input id={`edit-series-title-${series.id}`} bind:value={title} autocomplete="off" required />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-series-year-${series.id}`}>Year</Label>
        <Input id={`edit-series-year-${series.id}`} bind:value={year} inputmode="numeric" autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for={`edit-series-description-${series.id}`}>Description</Label>
        <Textarea id={`edit-series-description-${series.id}`} bind:value={description} rows={5} />
      </div>
      {#if locked}
        <div class="flex items-center justify-between gap-2 text-xs text-text-mute">
          <span>Locked — won't be overwritten by a metadata refresh.</span>
          <Button type="button" variant="secondary" size="mini" onclick={unlock} disabled={unlocking}>
            {#if unlocking}<Spinner />{/if}
            {unlocking ? 'Unlocking…' : 'Unlock'}
          </Button>
        </div>
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
