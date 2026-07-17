<script lang="ts">
  import { onMount } from 'svelte'
  import {
    getCollections,
    getCollectionMembership,
    addSeriesToCollection,
    removeSeriesFromCollection,
    createCollection,
    type Collection,
  } from './api'
  import { notify } from './notify'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Label } from './components/ui/label/index.js'
  import { Spinner } from './components/ui/spinner/index.js'
  import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
    DropdownMenuSeparator,
  } from './components/ui/dropdown-menu/index.js'
  import {
    AlertDialog,
    AlertDialogContent,
    AlertDialogHeader,
    AlertDialogTitle,
    AlertDialogDescription,
    AlertDialogFooter,
    AlertDialogCancel,
  } from './components/ui/alert-dialog/index.js'
  import CheckIcon from '@lucide/svelte/icons/check'

  let { seriesId }: { seriesId: string } = $props()

  let collections = $state<Collection[]>([])
  let membership = $state<Set<string>>(new Set())
  let loaded = $state(false)

  let createOpen = $state(false)
  let newName = $state('')
  let creating = $state(false)

  async function load() {
    try {
      const [cols, member] = await Promise.all([getCollections(), getCollectionMembership(seriesId)])
      collections = cols
      membership = new Set(member)
    } catch {
      /* best-effort — the menu just shows nothing */
    } finally {
      loaded = true
    }
  }

  onMount(load)

  async function toggle(c: Collection) {
    const isMember = membership.has(c.id)
    // Optimistic; revert on failure.
    const next = new Set(membership)
    if (isMember) next.delete(c.id)
    else next.add(c.id)
    membership = next
    try {
      if (isMember) await removeSeriesFromCollection(c.id, seriesId)
      else await addSeriesToCollection(c.id, seriesId)
    } catch (e) {
      membership = new Set(isMember ? [...membership, c.id] : [...membership].filter((x) => x !== c.id))
      notify.error(e instanceof Error ? e.message : 'Failed to update collection.')
    }
  }

  async function createAndAdd() {
    const name = newName.trim()
    if (name === '') {
      notify.error('A collection name is required.')
      return
    }
    creating = true
    try {
      const created = await createCollection(name)
      await addSeriesToCollection(created.id, seriesId)
      newName = ''
      createOpen = false
      notify.success(`Added to “${created.name}”.`)
      await load()
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to create collection.')
    } finally {
      creating = false
    }
  }
</script>

<DropdownMenu>
  <DropdownMenuTrigger>
    {#snippet child({ props })}
      <Button {...props} variant="outline">Add to collection</Button>
    {/snippet}
  </DropdownMenuTrigger>
  <DropdownMenuContent align="start" class="min-w-56">
    {#if !loaded}
      <div class="flex items-center gap-2 px-2 py-1.5 text-sm text-text-dim"><Spinner />Loading…</div>
    {:else}
      {#each collections as c (c.id)}
        <DropdownMenuItem onSelect={(e) => { e.preventDefault(); toggle(c) }}>
          <span class="flex w-full items-center justify-between gap-3">
            <span class="truncate">{c.name}</span>
            {#if membership.has(c.id)}<CheckIcon class="size-4 text-brand-soft" />{/if}
          </span>
        </DropdownMenuItem>
      {/each}
      {#if collections.length > 0}<DropdownMenuSeparator />{/if}
      <DropdownMenuItem onSelect={() => (createOpen = true)}>New collection…</DropdownMenuItem>
    {/if}
  </DropdownMenuContent>
</DropdownMenu>

<AlertDialog bind:open={createOpen}>
  <AlertDialogContent>
    <AlertDialogHeader>
      <AlertDialogTitle>New collection</AlertDialogTitle>
      <AlertDialogDescription>Creates the collection and adds this series to it.</AlertDialogDescription>
    </AlertDialogHeader>
    <form class="flex flex-col gap-3" onsubmit={(e) => { e.preventDefault(); createAndAdd() }}>
      <div class="flex flex-col gap-1.5">
        <Label for="new-collection-name">Name</Label>
        <Input id="new-collection-name" bind:value={newName} placeholder="e.g. Favourites" autocomplete="off" />
      </div>
      <AlertDialogFooter>
        <AlertDialogCancel type="button">Cancel</AlertDialogCancel>
        <Button type="submit" disabled={creating}>
          {#if creating}<Spinner />{/if}
          {creating ? 'Creating…' : 'Create & add'}
        </Button>
      </AlertDialogFooter>
    </form>
  </AlertDialogContent>
</AlertDialog>
