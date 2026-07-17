<script lang="ts">
  import { onMount } from 'svelte'
  import { push } from 'svelte-spa-router'
  import { getCollections, createCollection, type Collection } from '../lib/api'
  import { notify } from '../lib/notify'
  import PosterCard from '../lib/PosterCard.svelte'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Input } from '../lib/components/ui/input/index.js'
  import { Textarea } from '../lib/components/ui/textarea/index.js'
  import { Label } from '../lib/components/ui/label/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import {
    AlertDialog,
    AlertDialogContent,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogCancel,
  } from '../lib/components/ui/alert-dialog/index.js'

  let collections = $state<Collection[]>([])
  let loading = $state(true)
  let error = $state<string | null>(null)

  let createOpen = $state(false)
  let newName = $state('')
  let newDescription = $state('')
  let creating = $state(false)

  async function load() {
    loading = true
    error = null
    try {
      collections = await getCollections()
    } catch (e) {
      error = e instanceof Error ? e.message : 'Failed to load collections.'
    } finally {
      loading = false
    }
  }

  onMount(load)

  function subtitle(c: Collection): string {
    return c.itemCount === 1 ? '1 series' : `${c.itemCount} series`
  }

  async function create() {
    const name = newName.trim()
    if (name === '') {
      notify.error('A collection name is required.')
      return
    }
    creating = true
    try {
      const created = await createCollection(name, newDescription.trim() || null)
      createOpen = false
      newName = ''
      newDescription = ''
      notify.success('Collection created.')
      push(`/collections/${created.id}`)
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to create collection.')
    } finally {
      creating = false
    }
  }
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <div class="mb-5 flex items-center justify-between gap-4">
    <h1 class="text-[1.4rem]">Collections</h1>
    <Button onclick={() => (createOpen = true)}>New collection</Button>
  </div>

  {#if loading}
    <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
  {:else if error}
    <Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>
  {:else if collections.length === 0}
    <p class="muted">
      No collections yet. Create one, then add series to it from any series page or from the collection.
    </p>
  {:else}
    <div class="poster-grid">
      {#each collections as c (c.id)}
        <PosterCard
          title={c.name}
          subtitle={subtitle(c)}
          coverUrl={c.coverUrl}
          onclick={() => push(`/collections/${c.id}`)}
        />
      {/each}
    </div>
  {/if}
</section>

<AlertDialog bind:open={createOpen}>
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>New collection</AlertDialogTitle>
      <AlertDialogDescription>Group library series together. You can add series after creating it.</AlertDialogDescription>
    </AlertDialogHeader>
    <form class="flex flex-col gap-3" onsubmit={(e) => { e.preventDefault(); create() }}>
      <div class="flex flex-col gap-1.5">
        <Label for="collection-name">Name</Label>
        <Input id="collection-name" bind:value={newName} placeholder="e.g. Favourites" autocomplete="off" />
      </div>
      <div class="flex flex-col gap-1.5">
        <Label for="collection-description">Description (optional)</Label>
        <Textarea id="collection-description" bind:value={newDescription} rows={3} />
      </div>
      <AlertDialogFooter>
        <AlertDialogCancel type="button">Cancel</AlertDialogCancel>
        <Button type="submit" disabled={creating}>
          {#if creating}<Spinner />{/if}
          {creating ? 'Creating…' : 'Create'}
        </Button>
      </AlertDialogFooter>
    </form>
  </AlertDialogContent>
</AlertDialog>
