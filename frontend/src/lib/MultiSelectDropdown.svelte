<script lang="ts">
  import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuCheckboxItem,
  } from './components/ui/dropdown-menu/index.js'
  import { Button } from './components/ui/button/index.js'

  let {
    label,
    options,
    selected = $bindable([]),
    onchange,
  }: {
    label: string
    options: { id: string; name: string }[]
    selected?: string[]
    onchange?: () => void
  } = $props()

  function toggle(id: string) {
    selected = selected.includes(id) ? selected.filter((x) => x !== id) : [...selected, id]
    onchange?.()
  }
</script>

<DropdownMenu>
  <DropdownMenuTrigger>
    {#snippet child({ props })}
      <Button variant="outline" {...props}>
        {label}{selected.length > 0 ? ` (${selected.length})` : ''}
      </Button>
    {/snippet}
  </DropdownMenuTrigger>
  <DropdownMenuContent class="max-h-64 min-w-48 overflow-y-auto" align="start">
    {#if options.length === 0}
      <p class="p-[0.4rem] text-[0.82rem] text-text-mute">None yet.</p>
    {:else}
      {#each options as o (o.id)}
        <DropdownMenuCheckboxItem
          checked={selected.includes(o.id)}
          closeOnSelect={false}
          onCheckedChange={() => toggle(o.id)}
        >
          {o.name}
        </DropdownMenuCheckboxItem>
      {/each}
    {/if}
  </DropdownMenuContent>
</DropdownMenu>
