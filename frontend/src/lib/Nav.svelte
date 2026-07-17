<script lang="ts">
  import { onDestroy, onMount } from 'svelte'
  import { link, push, router } from 'svelte-spa-router'
  import { session, isAdmin, doLogout } from './session.svelte'
  import { getDownloads, type DownloadItem } from './api'
  import { progressByDownload } from './signalr.svelte'
  import NotificationBell from './NotificationBell.svelte'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from './components/ui/select/index.js'
  import { DropdownMenu, DropdownMenuTrigger, DropdownMenuContent, DropdownMenuItem, DropdownMenuSeparator } from './components/ui/dropdown-menu/index.js'
  import MenuIcon from '@lucide/svelte/icons/menu'
  import CheckIcon from '@lucide/svelte/icons/check'
  import { MODES, modeState, setMode, brandName, type MediaKind } from './mode.svelte'
  import { canBrowse, canBrowseKind } from './terms.svelte'

  // "Browse" searches the source catalogue; "Library" searches what's already downloaded. Which source
  // Browse hits is resolved there from the kind — the nav no longer needs to know (it used to hardcode
  // 'mangadex'). With no catalogue to browse (comics), Library is the only scope, so the picker goes away.
  const searchScopes = [
    { v: 'browse', l: 'Browse' },
    { v: 'library', l: 'Library' },
  ] as const

  let searchScope = $state<'browse' | 'library'>('browse')
  let searchQuery = $state('')

  let effectiveScope = $derived(canBrowse() ? searchScope : 'library')

  function submitSearch(e: SubmitEvent) {
    e.preventDefault()
    const q = searchQuery.trim()
    const target = effectiveScope === 'library' ? '/library' : '/browse'
    push(q ? `${target}?q=${encodeURIComponent(q)}` : target)
  }

  // Routes tied to one specific series/chapter/tag. A comic volume has no counterpart in the manga
  // library, so staying on it after a switch would show content from the library you just left. Every
  // other page (Home, Library, Activity, Admin, Profile, Settings) exists in both, so the user stays
  // put — App.svelte keys the Router on the mode, which remounts the page and refetches its content.
  const ENTITY_ROUTES = [/^\/series\//, /^\/genre\//, /^\/author\//, /^\/read\//, /^\/library\/.+/]

  function switchMode(kind: MediaKind) {
    if (kind === modeState.kind) return

    const from = router.location
    const stranded =
      ENTITY_ROUTES.some((r) => r.test(from)) || (from.startsWith('/browse') && !canBrowseKind(kind))

    // Navigate before switching: setMode remounts the router (App.svelte keys on it), so flipping first
    // would briefly render the page we're about to leave against the library it doesn't belong to.
    if (stranded) push('/')
    setMode(kind)
  }

  async function signOut() {
    await doLogout()
  }

  function navLinkClass(active: boolean): string {
    return `text-[0.9rem] no-underline ${active ? 'font-semibold text-foreground' : 'text-text-dim'}`
  }

  // Roomier than navLinkClass's compact desktop-nav sizing — this menu opens from a hamburger
  // button, so on mobile every row needs to be a comfortably tappable target.
  function mobileMenuItemClass(active: boolean): string {
    return `${navLinkClass(active)} px-3 py-3 text-base`
  }

  // Polled baseline of recent downloads, refined between polls by live SignalR progress — catches
  // both freshly-queued jobs (no push event exists for those) and fast status transitions.
  const ACTIVE = new Set(['Queued', 'Running'])
  let downloads = $state<DownloadItem[]>([])
  let pollTimer: ReturnType<typeof setInterval> | undefined

  async function pollDownloads() {
    try {
      downloads = await getDownloads()
    } catch {
      /* badge is best-effort */
    }
  }

  onMount(() => {
    pollDownloads()
    pollTimer = setInterval(pollDownloads, 10000)
  })
  onDestroy(() => clearInterval(pollTimer))

  let activeCount = $derived.by(() => {
    const seen = new Set<string>()
    let count = 0
    for (const d of downloads) {
      seen.add(d.id)
      if (ACTIVE.has(progressByDownload[d.id]?.status ?? d.status)) count++
    }
    for (const [id, p] of Object.entries(progressByDownload)) {
      if (!seen.has(id) && ACTIVE.has(p.status)) count++
    }
    return count
  })
</script>

<header class="sticky top-0 z-10 flex items-center gap-5 border-b border-border bg-surface-2 px-5 py-3">
  <DropdownMenu>
    <DropdownMenuTrigger>
      {#snippet child({ props })}
        <button {...props} class="cursor-pointer font-bold text-brand-soft" aria-label="Switch library">
          {brandName(modeState.kind)}
          <span class="ml-1 text-[0.7rem] opacity-70">▾</span>
        </button>
      {/snippet}
    </DropdownMenuTrigger>
    <DropdownMenuContent align="start" class="min-w-52">
      {#each MODES as m (m.kind)}
        <DropdownMenuItem class="px-3 py-2.5" onSelect={() => switchMode(m.kind)}>
          <span class="flex w-full items-center justify-between gap-3">
            <span>
              <span class="font-semibold">{m.brand}</span>
              <span class="ml-2 text-[0.75rem] text-text-dim">{m.label}</span>
            </span>
            {#if m.kind === modeState.kind}<CheckIcon class="size-4 text-brand-soft" />{/if}
          </span>
        </DropdownMenuItem>
      {/each}
    </DropdownMenuContent>
  </DropdownMenu>
  <nav class="hidden gap-4 xl:flex">
    <a href="/" use:link class={navLinkClass(router.location === '/')}>Home</a>
    {#if canBrowse()}
      <a href="/browse" use:link class={navLinkClass(router.location.startsWith('/browse'))}>Browse</a>
    {/if}
    <a href="/library" use:link class={navLinkClass(router.location.startsWith('/library'))}>Library</a>
    <a href="/collections" use:link class={navLinkClass(router.location.startsWith('/collections'))}>Collections</a>
    <a href="/activity" use:link class={navLinkClass(router.location === '/activity')}>
      Activity{#if activeCount > 0}
        <span class="ml-[0.3rem] inline-block rounded-full bg-brand-soft px-[0.4rem] py-[0.15rem] text-[0.68rem] leading-none font-bold text-surface-2">
          {activeCount > 99 ? '99+' : activeCount}
        </span>
      {/if}
    </a>
    {#if isAdmin()}
      <a href="/admin" use:link class={navLinkClass(router.location.startsWith('/admin'))}>Admin</a>
    {/if}
  </nav>

  <form class="flex gap-[0.4rem] max-[640px]:hidden" onsubmit={submitSearch} role="search">
    {#if canBrowse()}
      <Select type="single" bind:value={searchScope}>
        <SelectTrigger aria-label="Search scope">{searchScopes.find((s) => s.v === searchScope)?.l}</SelectTrigger>
        <SelectContent>
          {#each searchScopes as s (s.v)}<SelectItem value={s.v} label={s.l}>{s.l}</SelectItem>{/each}
        </SelectContent>
      </Select>
    {/if}
    <Input
      class="w-56 max-[900px]:w-36"
      type="search"
      placeholder={canBrowse() ? 'Search…' : 'Search library…'}
      bind:value={searchQuery}
    />
  </form>
  <div class="flex-1"></div>

  <!-- xl+: bell/profile/sign-out sit in the header. Below xl they move into the hamburger menu
       instead (see DropdownMenuContent below) so the header doesn't stay cluttered at narrow widths. -->
  <div class="hidden items-center gap-4 xl:flex">
    <NotificationBell />
    <a href="/profile" use:link class="muted max-w-[10rem] truncate text-[0.85rem] no-underline hover:text-foreground">
      {session.me?.email}
    </a>
    <Button variant="secondary" onclick={signOut}>Sign out</Button>
  </div>

  <DropdownMenu>
    <DropdownMenuTrigger>
      {#snippet child({ props })}
        <Button {...props} variant="outline" size="icon" class="xl:hidden" aria-label="Menu">
          <MenuIcon class="size-4" />
        </Button>
      {/snippet}
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="min-w-64">
      <DropdownMenuItem class={mobileMenuItemClass(router.location === '/')} onSelect={() => push('/')}>Home</DropdownMenuItem>
      {#if canBrowse()}
        <DropdownMenuItem class={mobileMenuItemClass(router.location.startsWith('/browse'))} onSelect={() => push('/browse')}>Browse</DropdownMenuItem>
      {/if}
      <DropdownMenuItem class={mobileMenuItemClass(router.location.startsWith('/library'))} onSelect={() => push('/library')}>Library</DropdownMenuItem>
      <DropdownMenuItem class={mobileMenuItemClass(router.location.startsWith('/collections'))} onSelect={() => push('/collections')}>Collections</DropdownMenuItem>
      <DropdownMenuItem class={mobileMenuItemClass(router.location === '/activity')} onSelect={() => push('/activity')}>
        Activity{#if activeCount > 0}
          <span class="ml-[0.3rem] inline-block rounded-full bg-brand-soft px-[0.4rem] py-[0.15rem] text-[0.68rem] leading-none font-bold text-surface-2">
            {activeCount > 99 ? '99+' : activeCount}
          </span>
        {/if}
      </DropdownMenuItem>
      {#if isAdmin()}
        <DropdownMenuItem class={mobileMenuItemClass(router.location.startsWith('/admin'))} onSelect={() => push('/admin')}>Admin</DropdownMenuItem>
      {/if}
      <DropdownMenuItem class={mobileMenuItemClass(router.location === '/profile')} onSelect={() => push('/profile')}>
        Profile
      </DropdownMenuItem>
      <DropdownMenuSeparator />
      {#each MODES as m (m.kind)}
        <DropdownMenuItem class="px-3 py-3 text-base" onSelect={() => switchMode(m.kind)}>
          <span class="flex w-full items-center justify-between gap-3">
            <span>{m.brand}</span>
            {#if m.kind === modeState.kind}<CheckIcon class="size-4 text-brand-soft" />{/if}
          </span>
        </DropdownMenuItem>
      {/each}
      <DropdownMenuSeparator />
      <div class="flex items-center justify-between gap-2 px-3 py-3">
        <span class="text-base text-text-dim">Notifications</span>
        <NotificationBell />
      </div>
      <DropdownMenuItem class="px-3 py-3 text-base" onSelect={signOut}>Sign out</DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</header>
