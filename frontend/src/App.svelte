<script lang="ts">
  import { onMount } from 'svelte'
  import Router from 'svelte-spa-router'
  import { session, loadSession } from './lib/session.svelte'
  import { startSignalR } from './lib/signalr.svelte'
  import { syncFromSession } from './lib/theme.svelte'
  import { modeState, syncFromSession as syncModeFromSession } from './lib/mode.svelte'
  import { syncFromSession as syncDashboardFromSession } from './lib/dashboard.svelte'
  import { Toaster } from './lib/components/ui/sonner/index.js'
  import { TooltipProvider } from './lib/components/ui/tooltip/index.js'
  import Login from './lib/Login.svelte'
  import Nav from './lib/Nav.svelte'
  import Home from './routes/Home.svelte'
  import Browse from './routes/Browse.svelte'
  import Series from './routes/Series.svelte'
  import GenrePage from './routes/GenrePage.svelte'
  import AuthorPage from './routes/AuthorPage.svelte'
  import Library from './routes/Library.svelte'
  import LibrarySeries from './routes/LibrarySeries.svelte'
  import Collections from './routes/Collections.svelte'
  import CollectionDetail from './routes/CollectionDetail.svelte'
  import Activity from './routes/Activity.svelte'
  import SourceSettings from './routes/SourceSettings.svelte'
  import Reader from './routes/Reader.svelte'
  import ReaderDispatch from './routes/ReaderDispatch.svelte'
  import Admin from './routes/Admin.svelte'
  import Profile from './routes/Profile.svelte'

  const routes = {
    '/': Home,
    '/browse': Browse,
    '/series/:sourceId/:seriesId': Series,
    '/genre/:sourceId/:tagId': GenrePage,
    '/author/:sourceId/:authorId': AuthorPage,
    '/library': Library,
    '/library/:id': LibrarySeries,
    '/collections': Collections,
    '/collections/:id': CollectionDetail,
    '/read/:chapterId': ReaderDispatch,
    '/preview/:sourceId/:chapterId': Reader,
    '/activity': Activity,
    '/admin': Admin,
    '/admin/:section': Admin,
    '/settings': SourceSettings,
    '/profile': Profile,
  }

  onMount(loadSession)

  // Connect the realtime hub once the user is authenticated.
  let started = false
  $effect(() => {
    if (session.me && !started) {
      started = true
      startSignalR()
    }
  })

  // Let the account's saved theme/mode (if any) win over the local paint-cache guess, once.
  let prefsSynced = false
  $effect(() => {
    if (session.me && !prefsSynced) {
      prefsSynced = true
      syncFromSession(session.me.theme)
      syncModeFromSession(session.me.preferredKind, session.me.homeAcrossLibraries)
      syncDashboardFromSession(session.me.dashboardLayout)
    }
  })
</script>

{#if session.loading}
  <p class="center muted">Loading…</p>
{:else if !session.me}
  <Login />
{:else}
  <TooltipProvider>
    <Nav />
    <!-- Keyed on the active library so switching mode remounts the current route. Without this, a page
         that stays put (Home, Library, Activity) would keep showing the other library's content: its data
         is fetched in onMount, and Svelte doesn't remount a component whose route hasn't changed. Nav sits
         outside the key so the header doesn't flicker. -->
    {#key modeState.kind}
      <Router {routes} />
    {/key}
  </TooltipProvider>
{/if}
<Toaster richColors position="bottom-right" />
