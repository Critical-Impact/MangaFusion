<script lang="ts">
  import { link } from 'svelte-spa-router'
  import { isAdmin } from '../lib/session.svelte'
  import { modeState } from '../lib/mode.svelte'
  import AdminUsers from './admin/AdminUsers.svelte'
  import AdminSettings from './admin/AdminSettings.svelte'
  import AdminTasks from './admin/AdminTasks.svelte'
  import AdminLocal from './admin/AdminLocal.svelte'
  import AdminMigrate from './admin/AdminMigrate.svelte'
  import AdminImport from './admin/AdminImport.svelte'
  import SourceSettings from './SourceSettings.svelte'

  let { params } = $props<{ params?: { section?: string } }>()

  // Migrate ingests the old MangaDex downloader's output — it matches files by their MangaDex chapter-UUID
  // filename prefix and dedups by scanlation group, neither of which has a comic or light-novel equivalent.
  // It's a manga-only tool (explicit allowlist, not "everything but comics"), so it's offered only there.
  const tabs = $derived([
    { id: 'users', label: 'Users' },
    { id: 'tasks', label: 'Tasks' },
    { id: 'local', label: 'Local' },
    ...(modeState.kind === 'manga' ? [{ id: 'migrate', label: 'Migrate' }] : []),
    { id: 'import', label: 'Import' },
    { id: 'settings', label: 'Settings' },
    { id: 'sources', label: 'Sources' },
  ])

  // The tab is hidden, but /admin/migrate is still reachable by URL (and stays in the address bar after a
  // mode switch), so fall back rather than rendering a tool that can't work here.
  const section = $derived.by(() => {
    const requested = params?.section ?? 'users'
    return tabs.some((t) => t.id === requested) ? requested : 'users'
  })

  function tabClass(active: boolean): string {
    return `-mb-px border-b-2 px-[0.9rem] py-2 text-[0.9rem] no-underline hover:text-foreground ${
      active ? 'border-brand-soft font-semibold text-foreground' : 'border-transparent text-text-dim'
    }`
  }
</script>

{#if !isAdmin()}
  <p class="muted px-5 py-8">Admins only.</p>
{:else}
  <section class="mx-auto max-w-[1100px] px-5 py-6">
    <h1 class="mb-4 text-[1.4rem]">Admin</h1>
    <nav class="mb-5 flex gap-[0.3rem] border-b border-border">
      {#each tabs as t (t.id)}
        <a href={`/admin/${t.id}`} use:link class={tabClass(section === t.id)}>{t.label}</a>
      {/each}
    </nav>

    <div>
      {#if section === 'tasks'}
        <AdminTasks />
      {:else if section === 'local'}
        <AdminLocal />
      {:else if section === 'migrate'}
        <AdminMigrate />
      {:else if section === 'import'}
        <AdminImport />
      {:else if section === 'settings'}
        <AdminSettings />
      {:else if section === 'sources'}
        <SourceSettings />
      {:else}
        <AdminUsers />
      {/if}
    </div>
  </section>
{/if}
