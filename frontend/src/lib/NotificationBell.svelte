<script lang="ts">
  import { onMount } from 'svelte'
  import { getNotifications, markAllNotificationsRead, type AppNotification } from './api'
  import { realtime } from './signalr.svelte'
  import { Popover, PopoverTrigger, PopoverContent } from './components/ui/popover/index.js'

  let unread = $state(0)
  let items = $state<AppNotification[]>([])
  let open = $state(false)
  let lastTick = 0

  async function refresh() {
    const r = await getNotifications()
    unread = r.unread
    items = r.items
  }

  onMount(refresh)

  // Refetch whenever a realtime notification arrives.
  $effect(() => {
    if (realtime.notificationTick !== lastTick) {
      lastTick = realtime.notificationTick
      refresh()
    }
  })

  $effect(() => {
    if (open && unread > 0) {
      markAllNotificationsRead()
      unread = 0
    }
  })

  function severityBorderStyle(severity: AppNotification['severity']): string {
    const color = severity === 'Error' ? 'var(--err)' : severity === 'Warning' ? 'var(--warn)' : 'var(--info)'
    return `border-left-color: ${color}`
  }
</script>

<Popover bind:open>
  <PopoverTrigger>
    {#snippet child({ props })}
      <button {...props} class="relative cursor-pointer border-0 bg-transparent p-[0.2rem] text-[1.1rem]" aria-label="Notifications">
        🔔{#if unread > 0}
          <span class="absolute -top-[2px] -right-[6px] rounded-full bg-brand-2 px-[0.3rem] py-[0.05rem] text-[0.62rem] font-bold text-white">
            {unread > 9 ? '9+' : unread}
          </span>
        {/if}
      </button>
    {/snippet}
  </PopoverTrigger>
  <PopoverContent class="max-h-[22rem] overflow-y-auto p-[0.4rem]" align="end">
    {#if items.length === 0}
      <p class="p-[0.8rem] text-center text-[0.85rem] text-text-mute">No notifications</p>
    {:else}
      {#each items.slice(0, 15) as n (n.id)}
        <div class="flex flex-col gap-[0.1rem] rounded-[6px] border-l-2 px-[0.6rem] py-[0.5rem] hover:bg-accent hover:text-accent-foreground" style={severityBorderStyle(n.severity)}>
          <span class="text-[0.85rem] font-semibold">{n.title}</span>
          {#if n.body}<span class="text-[0.78rem] text-text-mute">{n.body}</span>{/if}
        </div>
      {/each}
    {/if}
  </PopoverContent>
</Popover>
