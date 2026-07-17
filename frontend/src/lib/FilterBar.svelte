<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from './components/ui/select/index.js'

  type SortOpt = { v: string; l: string }

  let {
    query = $bindable(''),
    placeholder = 'Search…',
    onsubmit,
    showSearch = true,
    sort,
    order = $bindable(''),
    onsort,
    filters,
    trailing,
  }: {
    query?: string
    placeholder?: string
    onsubmit?: () => void
    showSearch?: boolean
    sort?: SortOpt[]
    order?: string
    onsort?: () => void
    filters?: Snippet
    trailing?: Snippet
  } = $props()

  function submit(e: SubmitEvent) {
    e.preventDefault()
    onsubmit?.()
  }
</script>

<div class="mb-5 flex flex-wrap items-end gap-4">
  {#if showSearch}
    <form class="flex min-w-[16rem] flex-1 gap-[0.6rem]" onsubmit={submit} role="search">
      <Input class="flex-1" type="search" {placeholder} bind:value={query} />
      <Button type="submit">Search</Button>
    </form>
  {/if}

  {#if filters}<div class="flex items-end gap-[0.6rem]">{@render filters()}</div>{/if}

  {#if sort}
    <label class="ml-auto flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
      Sort
      <Select type="single" bind:value={order} onValueChange={() => onsort?.()}>
        <SelectTrigger>{sort.find((o) => o.v === order)?.l ?? order}</SelectTrigger>
        <SelectContent>
          {#each sort as o (o.v)}<SelectItem value={o.v} label={o.l}>{o.l}</SelectItem>{/each}
        </SelectContent>
      </Select>
    </label>
  {/if}

  {#if trailing}<div>{@render trailing()}</div>{/if}
</div>
