<script lang="ts">
  import { onMount } from 'svelte'
  import { getCollections, type Collection } from './api'
  import { resolveItems, setLayoutForKind, type ResolvedDashboardItem } from './dashboard.svelte'
  import { Checkbox } from './components/ui/checkbox/index.js'
  import { Button } from './components/ui/button/index.js'

  let collections = $state<Collection[]>([])
  let items = $state<ResolvedDashboardItem[]>([])

  async function load() {
    try {
      collections = await getCollections()
    } catch {
      collections = []
    }
    items = resolveItems(collections)
  }

  onMount(load)

  function persist() {
    setLayoutForKind(items, collections)
  }

  function toggle(i: number) {
    items[i] = { ...items[i], visible: !items[i].visible }
    persist()
  }

  function move(i: number, delta: number) {
    const j = i + delta
    if (j < 0 || j >= items.length) return
    const next = [...items]
    ;[next[i], next[j]] = [next[j], next[i]]
    items = next
    persist()
  }
</script>

<div class="flex flex-col gap-2">
  {#each items as item, i (item.key)}
    <div class="flex items-center gap-3 rounded-md border border-border px-3 py-2">
      <Checkbox checked={item.visible} onCheckedChange={() => toggle(i)} aria-label={`Show ${item.label}`} />
      <span class="flex-1 truncate text-[0.85rem]">
        {item.label}
        <span class="ml-1.5 text-[0.7rem] text-text-mute">{item.type === 'rail' ? 'Rail' : 'Collection'}</span>
      </span>
      <div class="flex gap-1">
        <Button variant="outline" size="mini" disabled={i === 0} onclick={() => move(i, -1)} aria-label="Move up">↑</Button>
        <Button variant="outline" size="mini" disabled={i === items.length - 1} onclick={() => move(i, 1)} aria-label="Move down">↓</Button>
      </div>
    </div>
  {/each}
</div>
