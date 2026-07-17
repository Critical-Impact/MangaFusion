<script lang="ts">
  import { onMount } from 'svelte'
  import { push } from 'svelte-spa-router'
  import {
    getCollection,
    updateCollection,
    deleteCollection,
    removeSeriesFromCollection,
    reorderCollection,
    uploadCollectionCover,
    clearCollectionCover,
    MEMBER_SORTS,
    DASHBOARD_FILTERS,
    type CollectionDetail,
  } from '../lib/api'
  import { notify } from '../lib/notify'
  import Cover from '../lib/Cover.svelte'
  import PosterCard from '../lib/PosterCard.svelte'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Input } from '../lib/components/ui/input/index.js'
  import { Textarea } from '../lib/components/ui/textarea/index.js'
  import { Label } from '../lib/components/ui/label/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import {
    AlertDialog,
    AlertDialogTrigger,
    AlertDialogContent,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogCancel,
    AlertDialogAction,
  } from '../lib/components/ui/alert-dialog/index.js'

  let { params } = $props<{ params: { id: string } }>()

  let detail = $state<CollectionDetail | null>(null)
  let loading = $state(true)
  let error = $state<string | null>(null)

  let editOpen = $state(false)
  let editName = $state('')
  let editDescription = $state('')
  let saving = $state(false)
  let coverInput = $state<HTMLInputElement | null>(null)
  let busyCover = $state(false)

  const isManual = $derived(detail?.memberSort === 'Manual')

  async function load() {
    loading = true
    error = null
    try {
      detail = await getCollection(params.id)
    } catch (e) {
      error = e instanceof Error ? e.message : 'Failed to load collection.'
    } finally {
      loading = false
    }
  }

  onMount(load)

  // The update endpoint takes name+description+sort+filter together, so every change sends the current values.
  async function save(
    overrides: Partial<Pick<CollectionDetail, 'name' | 'description' | 'memberSort' | 'dashboardFilter'>>,
  ) {
    if (!detail) return
    const name = (overrides.name ?? detail.name).trim()
    if (name === '') {
      notify.error('A collection name is required.')
      return
    }
    const description = overrides.description !== undefined ? overrides.description : detail.description
    const memberSort = overrides.memberSort ?? detail.memberSort
    const dashboardFilter = overrides.dashboardFilter ?? detail.dashboardFilter
    await updateCollection(detail.id, name, description?.trim() || null, memberSort, dashboardFilter)
    await load()
  }

  function openEdit() {
    if (!detail) return
    editName = detail.name
    editDescription = detail.description ?? ''
    editOpen = true
  }

  async function saveEdit() {
    saving = true
    try {
      await save({ name: editName, description: editDescription })
      editOpen = false
      notify.success('Collection updated.')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to update collection.')
    } finally {
      saving = false
    }
  }

  async function changeSort(value: string) {
    try {
      await save({ memberSort: value })
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to change sort.')
    }
  }

  async function changeFilter(value: string) {
    try {
      await save({ dashboardFilter: value })
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to change dashboard filter.')
    }
  }

  async function removeMember(seriesId: string) {
    if (!detail) return
    try {
      await removeSeriesFromCollection(detail.id, seriesId)
      await load()
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to remove series.')
    }
  }

  async function move(index: number, delta: number) {
    if (!detail) return
    const ids = detail.members.map((m) => m.seriesId)
    const target = index + delta
    if (target < 0 || target >= ids.length) return
    ;[ids[index], ids[target]] = [ids[target], ids[index]]
    try {
      await reorderCollection(detail.id, ids)
      await load()
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to reorder.')
    }
  }

  async function onCoverPicked(e: Event) {
    const input = e.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = '' // allow re-picking the same file
    if (!detail || !file) return
    busyCover = true
    try {
      await uploadCollectionCover(detail.id, file)
      await load()
      notify.success('Cover updated.')
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to upload cover.')
    } finally {
      busyCover = false
    }
  }

  async function resetCover() {
    if (!detail) return
    busyCover = true
    try {
      await clearCollectionCover(detail.id)
      await load()
      notify.success('Reverted to the auto-generated cover.')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to reset cover.')
    } finally {
      busyCover = false
    }
  }

  async function remove() {
    if (!detail) return
    try {
      await deleteCollection(detail.id)
      notify.success('Collection deleted.')
      push('/collections')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to delete collection.')
    }
  }
</script>

{#if loading}
  <p class="muted flex items-center gap-2 px-5 py-8"><Spinner />Loading…</p>
{:else if error && !detail}
  <div class="px-5 py-8">
    <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
  </div>
{:else if detail}
  <section class="mx-auto max-w-[1100px] px-5 py-6">
    <div class="mb-6 flex gap-6 max-[640px]:flex-col">
      <div class="flex-[0_0_170px]" style="--cover-w:170px">
        <Cover src={detail.coverUrl} alt={detail.name} />
      </div>
      <div class="min-w-0 flex-1">
        <h1 class="mb-1 text-2xl">{detail.name}</h1>
        <p class="muted">{detail.members.length === 1 ? '1 series' : `${detail.members.length} series`}</p>
        {#if detail.description}
          <p class="mt-2 text-sm text-text-2">{detail.description}</p>
        {/if}

        <div class="my-3 flex flex-wrap items-center gap-2.5 border-y border-border py-2.5">
          <label class="flex items-center gap-2 text-[0.8rem] text-text-dim">
            Sort
            <Select type="single" value={detail.memberSort} onValueChange={(v) => changeSort(v)}>
              <SelectTrigger class="w-auto min-w-40">
                {MEMBER_SORTS.find((s) => s.v === detail?.memberSort)?.l ?? detail.memberSort}
              </SelectTrigger>
              <SelectContent>
                {#each MEMBER_SORTS as s (s.v)}<SelectItem value={s.v} label={s.l}>{s.l}</SelectItem>{/each}
              </SelectContent>
            </Select>
          </label>

          <label class="flex items-center gap-2 text-[0.8rem] text-text-dim">
            On dashboard
            <Select type="single" value={detail.dashboardFilter} onValueChange={(v) => changeFilter(v)}>
              <SelectTrigger class="w-auto min-w-44">
                {DASHBOARD_FILTERS.find((f) => f.v === detail?.dashboardFilter)?.l ?? detail.dashboardFilter}
              </SelectTrigger>
              <SelectContent>
                {#each DASHBOARD_FILTERS as f (f.v)}<SelectItem value={f.v} label={f.l}>{f.l}</SelectItem>{/each}
              </SelectContent>
            </Select>
          </label>

          <Button variant="outline" size="mini" onclick={openEdit}>Edit</Button>

          <Button variant="outline" size="mini" disabled={busyCover} onclick={() => coverInput?.click()}>
            {#if busyCover}<Spinner />{/if}
            Upload cover
          </Button>
          {#if detail.coverIsCustom}
            <Button variant="outline" size="mini" disabled={busyCover} onclick={resetCover}>Reset cover</Button>
          {/if}
          <input
            bind:this={coverInput}
            type="file"
            accept="image/*"
            class="hidden"
            onchange={onCoverPicked}
          />

          <AlertDialog>
            <AlertDialogTrigger>
              {#snippet child({ props })}
                <Button {...props} variant="secondary" size="mini" class="ml-auto border-danger-border text-destructive hover:border-destructive">
                  Delete collection
                </Button>
              {/snippet}
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete {detail.name}?</AlertDialogTitle>
                <AlertDialogDescription>
                  This removes the collection. The series in it stay in your library. This cannot be undone.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onclick={remove}>Delete</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </div>
        {#if isManual}
          <p class="muted text-[0.75rem]">Manual order — use the arrows on each series to reorder.</p>
        {/if}
      </div>
    </div>

    {#if detail.members.length === 0}
      <p class="muted">
        This collection is empty. Open a series in your library and use “Add to collection”.
      </p>
    {:else}
      <div class="poster-grid">
        {#each detail.members as m, i (m.seriesId)}
          <div class="flex flex-col gap-1.5">
            <PosterCard title={m.title} coverUrl={m.coverUrl} href={`/library/${m.seriesId}`} />
            <div class="flex items-center justify-between gap-1 px-[var(--poster-pad)]">
              {#if isManual}
                <div class="flex gap-1">
                  <Button variant="outline" size="mini" disabled={i === 0} onclick={() => move(i, -1)} aria-label="Move left">←</Button>
                  <Button variant="outline" size="mini" disabled={i === detail.members.length - 1} onclick={() => move(i, 1)} aria-label="Move right">→</Button>
                </div>
              {:else}
                <span></span>
              {/if}
              <Button variant="outline" size="mini" onclick={() => removeMember(m.seriesId)}>Remove</Button>
            </div>
          </div>
        {/each}
      </div>
    {/if}
  </section>
{/if}

<AlertDialog bind:open={editOpen}>
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>Edit collection</AlertDialogTitle>
    </AlertDialogHeader>
    <form class="flex flex-col gap-3" onsubmit={(e) => { e.preventDefault(); saveEdit() }}>
      <div class="flex flex-col gap-1.5">
        <Label for="edit-collection-name">Name</Label>
        <Input id="edit-collection-name" bind:value={editName} autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for="edit-collection-description">Description (optional)</Label>
        <Textarea id="edit-collection-description" bind:value={editDescription} rows={3} />
      </div>
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
