<script lang="ts">
  import { onMount } from 'svelte'
  import { link, push } from 'svelte-spa-router'
  import {
    getLibrarySeries,
    downloadChapter,
    downloadMissing,
    followSeries,
    unfollowSeries,
    setPreferredGroups,
    setPolicy,
    setChapterSortMode,
    scanSeries,
    refreshSeriesMetadata,
    uploadSeriesCover,
    unlockSeriesCover,
    deleteSeries,
    deleteChapter,
    addReading,
    dismissReading,
    authorHref,
    genreSourceHref,
    getSources,
    type LibrarySeriesDetail,
    type LibraryChapter,
    type TagInfo,
    type SourceSummary,
  } from '../lib/api'
  import { isAdmin, session } from '../lib/session.svelte'
  import { progressByChapter } from '../lib/signalr.svelte'
  import { notify } from '../lib/notify'
  import { languagesState, ensureLanguagesLoaded, languageName } from '../lib/languages.svelte'
  import { isComic } from '../lib/mode.svelte'
  import { t, facetGroups } from '../lib/terms.svelte'
  import Cover from '../lib/Cover.svelte'
  import AddToCollection from '../lib/AddToCollection.svelte'
  import EditChapterDialog from '../lib/EditChapterDialog.svelte'
  import EditSeriesDialog from '../lib/EditSeriesDialog.svelte'
  import MultiSelectDropdown from '../lib/MultiSelectDropdown.svelte'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Input } from '../lib/components/ui/input/index.js'
  import { Checkbox } from '../lib/components/ui/checkbox/index.js'
  import { Label } from '../lib/components/ui/label/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import { Badge, badgeVariants } from '../lib/components/ui/badge/index.js'
  import { Card, CardContent, CardHeader, CardTitle, CardAction } from '../lib/components/ui/card/index.js'
  import { Separator } from '../lib/components/ui/separator/index.js'
  import { Collapsible, CollapsibleTrigger, CollapsibleContent } from '../lib/components/ui/collapsible/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'
  import { Tooltip, TooltipTrigger, TooltipContent } from '../lib/components/ui/tooltip/index.js'
  import { cn } from '../lib/utils.js'
  import {
    DropdownMenu,
    DropdownMenuTrigger,
    DropdownMenuContent,
    DropdownMenuItem,
  } from '../lib/components/ui/dropdown-menu/index.js'
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

  ensureLanguagesLoaded()

  let { params } = $props<{ params: { id: string } }>()

  let detail = $state<LibrarySeriesDetail | null>(null)
  let sources = $state<SourceSummary[]>([])
  getSources()
    .then((s) => (sources = s))
    .catch(() => {})
  let loading = $state(true)
  let error = $state('')
  let lang = $state('')
  let preferred = $state<string[]>([])
  let refreshing = false

  // Follow editor. This quick-setup UI only ever offers "not following" or "following with
  // auto-download on" — there's no separate on/off flag exposed here, so followed always implies
  // auto-download; emptying the language list (or hitting the X) unfollows outright instead of
  // leaving a dangling "following, nothing selected" state.
  let followLangs = $state<string[]>([])
  // Policy editor (admin-only, separate from the follow above — see Series.AutoDownload).
  let policyAuto = $state(false)
  let policyGrace = $state<number | null>(null)
  let policyLangs = $state<string[]>([])
  // Chapter sort mode editor (admin-only) — see setChapterSortMode.
  let chapterSortMode = $state('Absolute')
  let scanning = $state(false)
  let queuingMissing = $state(false)
  let groupFilter = $state<string | null>(null)
  // Admin-only: reveals the editing surfaces (metadata/cover/groups/policy/sort-mode/per-chapter).
  let editMode = $state(false)

  // A manually-imported series: no remote source to download from or scan.
  const isLocal = $derived(detail?.sourceId === 'local')

  // Whether this series has any manually-imported chapter — the reliable per-chapter signal (not
  // isLocal/sourceId, which reflects the series' metadata-primary source and is misleading for e.g.
  // a ComicVine-matched comic whose chapters are all still manually imported). Gates the sort-mode
  // control, since VolumeThenChapter mode only ever matters for manually-imported content.
  const hasManualChapters = $derived((detail?.chapters ?? []).some((c) => c.canEdit))

  const sourceCaps = $derived(sources.find((s) => s.id === detail?.sourceId)?.capabilities ?? [])

  // Downloading and scanning are separate capabilities, and a source can genuinely have one without
  // the other. ComicVine lists issues (Chapters) but serves no page images (no Download), so a comic
  // can be rescanned for new issues while every download control stays correctly unavailable. Both are
  // also broader than isLocal: a series matched to MangaUpdates during import (metadata-only) has a real
  // sourceId but can do neither.
  const canDownload = $derived(sourceCaps.includes('Download'))
  const canScan = $derived(sourceCaps.includes('Chapters'))

  // Immediate click feedback: set true on click, cleared once real SignalR progress arrives (there's
  // a gap between queuing the job and the first progress event).
  let queuing = $state<Record<string, boolean>>({})

  // Languages this series actually has imported chapters in — drives the chapter list's view
  // filter (and "download missing"), which only ever makes sense scoped to what's been imported.
  const observedLanguages = $derived(
    detail ? [...new Set(detail.chapters.map((c) => c.language))].sort() : [],
  )
  // Falls back through: the user's profile default -> the page's active language filter -> the
  // first language this series has chapters in -> "en", so the quick-follow button always has
  // something sensible to pre-fill.
  const effectiveDefaultLang = $derived(session.me?.defaultLanguage || lang || observedLanguages[0] || 'en')
  // Follow/policy pickers offer the full known-language list — not just what's been imported so
  // far — so a language can be selected before any release exists in it (the whole point of
  // auto-download: catch a language becoming available later). Still unioned with observed
  // languages and whatever's currently selected, in case a legacy/odd code isn't in the known list.
  const languageOptions = $derived.by(() => {
    const codes = new Set([
      ...languagesState.items.map((l) => l.code),
      ...observedLanguages,
      ...followLangs,
      ...policyLangs,
      effectiveDefaultLang,
    ])
    return [...codes]
      .map((id) => ({ id, name: languageName(id) }))
      .sort((a, b) => a.name.localeCompare(b.name))
  })
  // Mirrors the backend's chapter ordering (LibraryEndpoints.OrderChapters / ReaderService): Absolute
  // sorts purely by chapter number; VolumeThenChapter sorts by volume first, and — within a volume —
  // puts the whole-volume row itself (blank number) before any numbered extra tagged to it.
  function compareChapters(a: LibraryChapter, b: LibraryChapter, mode: string): number {
    if (mode === 'VolumeThenChapter') {
      const av = a.volumeSort ?? Number.MAX_VALUE
      const bv = b.volumeSort ?? Number.MAX_VALUE
      if (av !== bv) return av - bv
      const aIsVolume = a.number === null ? 0 : 1
      const bIsVolume = b.number === null ? 0 : 1
      if (aIsVolume !== bIsVolume) return aIsVolume - bIsVolume
    }
    return (a.numberSort ?? Number.MAX_VALUE) - (b.numberSort ?? Number.MAX_VALUE)
  }

  // The chapter to send the reader to: the first downloaded, unfinished chapter in order, or (if
  // everything downloaded has been read) the last downloaded chapter, for re-reading.
  const nextToRead = $derived.by(() => {
    const downloaded = (detail?.chapters ?? [])
      .filter((c) => c.downloaded)
      .sort((a, b) => compareChapters(a, b, detail?.sortMode ?? 'Absolute'))
    if (downloaded.length === 0) return null
    return downloaded.find((c) => !c.completed) ?? downloaded[downloaded.length - 1]
  })
  const hasReadingProgress = $derived(
    (detail?.chapters ?? []).some((c) => c.downloaded && (c.completed || c.pageIndex > 0)),
  )
  const availableGroups = $derived(
    detail
      ? [...new Set(detail.chapters.flatMap((c) => c.releases.filter((r) => !r.isExternal).map((r) => r.groupKey).filter((g): g is string => !!g)))].sort()
      : [],
  )
  const visibleChapters = $derived(
    (detail?.chapters ?? [])
      .filter((c) => !lang || c.language === lang)
      .filter((c) => !groupFilter || chapterGroups(c).includes(groupFilter))
      .sort((a, b) => compareChapters(a, b, detail?.sortMode ?? 'Absolute')),
  )

  // Per-group chapter counts for the selected language (drives the groups panel).
  const groupStats = $derived.by(() => {
    const counts = new Map<string, number>()
    for (const c of detail?.chapters ?? []) {
      if (lang && c.language !== lang) continue
      for (const g of chapterGroups(c)) counts.set(g, (counts.get(g) ?? 0) + 1)
    }
    return [...counts.entries()].sort((a, b) => b[1] - a[1])
  })

  function chapterGroups(c: LibraryChapter): string[] {
    return [...new Set(c.releases.filter((r) => !r.isExternal && r.groupKey).map((r) => r.groupKey as string))]
  }

  function chapterLabel(c: LibraryChapter): string {
    const parts: string[] = []
    if (c.volume) parts.push(`Vol. ${c.volume}`)
    if (c.number) parts.push(`Ch. ${c.number}`)
    if (parts.length === 0) parts.push('Oneshot')
    return parts.join(' ')
  }

  function formatDate(iso: string | null): string {
    if (!iso) return ''
    return new Date(iso).toLocaleDateString()
  }

  function libraryHref(tag: TagInfo): string {
    return `/library?${tag.group.toLowerCase()}=${tag.id}`
  }

  // Splits the flat tag list into labeled sections by tag.group — the known browse/library facet
  // groups first (Genre/Theme for manga, Publisher/Character/Concept for comics), in that order, then
  // any other group the source sends (e.g. MangaDex's "format"/"content") appended under its own
  // title-cased label rather than getting silently dropped.
  //
  // Grouped case-insensitively: a tag's group is meant to be a stable identifier, not display text, so
  // two casings of the same group (e.g. a badly-cased source, or an older row from before a source
  // normalized its casing) are the same section rather than two — which also matters mechanically,
  // since two sections that stringify to the same label would otherwise collide as duplicate #each keys.
  let tagSections = $derived.by(() => {
    if (!detail) return []
    const byGroup = new Map<string, TagInfo[]>()
    for (const tag of detail.tags) {
      const key = tag.group.toLowerCase()
      const list = byGroup.get(key)
      if (list) list.push(tag)
      else byGroup.set(key, [tag])
    }
    const sections: { label: string; tags: TagInfo[] }[] = []
    for (const { group, label } of facetGroups()) {
      const key = group.toLowerCase()
      const tags = byGroup.get(key)
      if (tags?.length) {
        sections.push({ label, tags })
        byGroup.delete(key)
      }
    }
    for (const [group, tags] of byGroup) {
      sections.push({ label: group.charAt(0).toUpperCase() + group.slice(1), tags })
    }
    return sections
  })

  onMount(load)

  async function load() {
    loading = true
    try {
      detail = await getLibrarySeries(params.id)
      preferred = [...detail.preferredGroups]
      followLangs = [...detail.followLanguages]
      policyAuto = detail.autoDownload
      policyGrace = detail.gracePeriodDays
      policyLangs = [...detail.seriesLanguages]
      chapterSortMode = detail.sortMode
      if (observedLanguages.length && !observedLanguages.includes(lang)) {
        lang = observedLanguages.includes('en') ? 'en' : observedLanguages[0]
      }
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to load series.'
    } finally {
      loading = false
    }
  }

  async function refresh() {
    if (refreshing) return
    refreshing = true
    try {
      detail = await getLibrarySeries(params.id)
    } finally {
      refreshing = false
    }
  }

  // Refetch when a chapter of this series finishes downloading, to pick up the new state.
  $effect(() => {
    for (const c of detail?.chapters ?? []) {
      const p = progressByChapter[c.id]
      if (p && p.status === 'Completed' && !c.downloaded) {
        refresh()
        break
      }
    }
  })

  function hasDownloadable(c: LibraryChapter) {
    return c.releases.some((r) => !r.isExternal)
  }

  // The most-preferred group available for a chapter (used to hint an upgrade).
  function preferredAvailable(c: LibraryChapter): string | null {
    const groups = c.releases.filter((r) => !r.isExternal).map((r) => r.groupKey)
    for (const g of preferred) {
      if (groups.includes(g)) return g
    }
    return null
  }

  function upgradeAvailable(c: LibraryChapter): string | null {
    if (!c.downloaded) return null
    const best = preferredAvailable(c)
    return best && best !== c.activeGroup ? best : null
  }

  async function download(c: LibraryChapter) {
    queuing[c.id] = true
    try {
      await downloadChapter(c.id)
    } catch (err) {
      queuing[c.id] = false
      notify.error(err instanceof Error ? err.message : 'Download failed.')
    }
  }

  // Once the download engine emits its first progress event for a chapter, hand off to progressText.
  $effect(() => {
    for (const id of Object.keys(queuing)) {
      if (queuing[id] && progressByChapter[id]) queuing[id] = false
    }
  })

  async function queueMissing() {
    queuingMissing = true
    try {
      const { queued } = await downloadMissing(params.id, lang ? [lang] : [])
      if (queued === 0) {
        notify.info('Nothing to download.')
      } else {
        notify.success(`Queued ${queued} chapter${queued === 1 ? '' : 's'}.`)
      }
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to queue downloads.')
    } finally {
      queuingMissing = false
    }
  }

  // A follow with no languages selected can't drive any downloads, so emptying the language list
  // (e.g. via the language menu) unfollows outright rather than leaving a dangling, useless follow.
  async function saveFollow() {
    if (!detail) return
    if (followLangs.length === 0) {
      await unfollowSeries(detail.id)
    } else {
      await followSeries(detail.id, followLangs, true)
    }
    await refresh()
  }

  async function removeFollow() {
    if (!detail) return
    followLangs = []
    await unfollowSeries(detail.id)
    await refresh()
  }

  async function quickAutoDownload() {
    followLangs = [effectiveDefaultLang]
    await saveFollow()
  }

  async function savePolicy() {
    await setPolicy(params.id, policyGrace, policyAuto, policyLangs)
    await refresh()
  }

  async function saveSortMode() {
    try {
      await setChapterSortMode(params.id, chapterSortMode)
      await refresh()
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to change chapter sort mode.')
    }
  }

  async function scanNow() {
    scanning = true
    try {
      await scanSeries(params.id)
      notify.success('Scan queued — new chapters will appear shortly.')
    } finally {
      scanning = false
    }
  }

  let refreshingMetadata = $state(false)
  async function refreshMetadataNow() {
    refreshingMetadata = true
    try {
      await refreshSeriesMetadata(params.id)
      await refresh()
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to refresh metadata.')
    } finally {
      refreshingMetadata = false
    }
  }

  let uploadingCover = $state(false)
  let unlockingCover = $state(false)
  let coverFileInput = $state<HTMLInputElement | null>(null)
  async function onCoverFileChosen(e: Event) {
    const file = (e.target as HTMLInputElement).files?.[0]
    if (!file) return
    uploadingCover = true
    try {
      await uploadSeriesCover(params.id, file)
      await refresh()
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to upload cover.')
    } finally {
      uploadingCover = false
      if (coverFileInput) coverFileInput.value = ''
    }
  }
  async function unlockCoverNow() {
    unlockingCover = true
    try {
      await unlockSeriesCover(params.id)
      await refresh()
    } catch (err) {
      notify.error(err instanceof Error ? err.message : 'Failed to unlock cover.')
    } finally {
      unlockingCover = false
    }
  }

  let deletingSeries = $state(false)
  async function deleteSeriesNow() {
    deletingSeries = true
    try {
      await deleteSeries(params.id)
      push('/library')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to delete series.')
      deletingSeries = false
    }
  }

  let deletingChapter = $state<Record<string, boolean>>({})
  async function deleteChapterNow(c: LibraryChapter) {
    deletingChapter[c.id] = true
    try {
      await deleteChapter(c.id)
      await refresh()
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to delete chapter.')
    } finally {
      deletingChapter[c.id] = false
    }
  }

  async function toggleReading() {
    if (!detail) return
    if (detail.reading) {
      await dismissReading(detail.id)
    } else {
      await addReading(detail.id)
    }
    await refresh()
  }

  function moveGroup(i: number, dir: -1 | 1) {
    const j = i + dir
    if (j < 0 || j >= preferred.length) return
    ;[preferred[i], preferred[j]] = [preferred[j], preferred[i]]
  }
  function removeGroup(i: number) {
    preferred.splice(i, 1)
  }
  let addGroupValue = $state('')
  function addGroup(g: string) {
    if (g && !preferred.includes(g)) preferred.push(g)
    addGroupValue = ''
  }
  async function saveGroups() {
    await setPreferredGroups(params.id, preferred)
    await refresh()
  }

  function progressText(c: LibraryChapter): string | null {
    const p = progressByChapter[c.id]
    if (!p || p.status === 'Completed') return null
    if (p.status === 'Queued') return 'Queued…'
    if (p.status === 'Running') return p.pagesTotal > 0 ? `Downloading ${p.pagesDone}/${p.pagesTotal}` : 'Preparing…'
    if (p.status === 'Failed') return 'Failed'
    return p.status
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
    <div class="mb-5 flex gap-6 max-[640px]:flex-col">
      <div class="flex-[0_0_170px]" style="--cover-w:170px">
        <Cover src={detail.coverUrl} alt={detail.title} />
        {#if isAdmin() && editMode}
          <input
            bind:this={coverFileInput}
            type="file"
            accept="image/*"
            class="hidden"
            onchange={onCoverFileChosen}
          />
          <div class="mt-1.5 flex items-center gap-1.5">
            <Button
              variant="secondary"
              size="mini"
              disabled={uploadingCover}
              onclick={() => coverFileInput?.click()}
            >
              {#if uploadingCover}<Spinner />{/if}
              {uploadingCover ? 'Uploading…' : 'Change cover'}
            </Button>
            {#if detail.coverLocked}
              <Button variant="secondary" size="mini" disabled={unlockingCover} onclick={unlockCoverNow}>
                {#if unlockingCover}<Spinner />{/if}
                Unlock
              </Button>
            {/if}
          </div>
        {/if}
      </div>
      <div class="min-w-0 flex-1">
        <div class="mb-1 flex items-center gap-2">
          <h1 class="text-2xl">{detail.title}</h1>
          {#if isAdmin() && editMode}
            <EditSeriesDialog series={detail} onSaved={refresh} />
          {/if}
        </div>
        <p class="muted">
          {detail.status}{detail.year ? ` · ${detail.year}` : ''} · {detail.contentRating}
          {#if detail.sourceName}
            · {#if detail.siteUrl}
              <a
                class="text-brand-soft no-underline hover:underline"
                href={detail.siteUrl}
                target="_blank"
                rel="noreferrer noopener"
              >{detail.sourceName} ↗</a>
            {:else}
              {detail.sourceName}
            {/if}
          {/if}
          {#if isLocal}
            · <Badge variant="outline" class="text-info border-info/40">Local</Badge>
          {/if}
        </p>

        <div class="my-3 flex flex-wrap items-center gap-2.5 border-y border-border py-2.5">
          {#if nextToRead}
            <Button href={`/read/${nextToRead.id}`}>
              {hasReadingProgress ? 'Continue Reading' : 'Read'}
            </Button>
          {/if}
          <Tooltip>
            <TooltipTrigger>
              {#snippet child({ props })}
                <Button {...props} variant={detail?.reading ? 'secondary' : 'outline'} onclick={toggleReading}>
                  {detail?.reading ? '✓ Reading — remove' : '+ Add as reading'}
                </Button>
              {/snippet}
            </TooltipTrigger>
            <TooltipContent>
              {detail.reading
                ? 'Remove this series from your reading list.'
                : 'Add this series to your reading list, so it shows up in Continue Reading even before you download anything.'}
            </TooltipContent>
          </Tooltip>
          <AddToCollection seriesId={detail.id} />
          {#if !detail.followed}
            <Tooltip>
              <TooltipTrigger>
                {#snippet child({ props })}
                  <!-- A disabled <button> never fires hover/focus events (even bubbled), so the
                       tooltip trigger goes on this wrapping span instead — pointer-events-none on
                       the disabled button lets the mouse "see through" to the span underneath. -->
                  <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
                  <span {...props} class="inline-flex" tabindex={canDownload ? -1 : 0}>
                    <Button onclick={quickAutoDownload} disabled={!canDownload}>
                      Auto-download in {effectiveDefaultLang}
                    </Button>
                  </span>
                {/snippet}
              </TooltipTrigger>
              <TooltipContent>
                {canDownload
                  ? `Follow this series and automatically queue new ${effectiveDefaultLang} releases as they're found.`
                  : "This series is manually downloaded — there's no source to check for new releases, so auto-download isn't available."}
              </TooltipContent>
            </Tooltip>
          {:else}
            <Badge variant="outline" class={canDownload ? 'text-ok border-ok-border' : 'text-text-mute'}>
              Auto-downloading · {followLangs.join(', ') || '—'}
              <button
                type="button"
                class="cursor-pointer border-0 bg-transparent p-0 leading-none opacity-70 hover:opacity-100 [font:inherit]"
                onclick={removeFollow}
                title="Unfollow"
              >✕</button>
            </Badge>
            {#if canDownload}
              <MultiSelectDropdown
                label="Languages"
                options={languageOptions}
                bind:selected={followLangs}
                onchange={saveFollow}
              />
            {/if}
          {/if}
          {#if isAdmin()}
            <Button
              variant={editMode ? 'secondary' : 'outline'}
              size="mini"
              class="ml-auto"
              onclick={() => (editMode = !editMode)}
            >
              {editMode ? '✓ Editing — done' : '✎ Edit'}
            </Button>
            <AlertDialog>
              <AlertDialogTrigger>
                {#snippet child({ props })}
                  <Button
                    {...props}
                    variant="secondary"
                    size="mini"
                    class="border-danger-border text-destructive hover:border-destructive"
                    disabled={deletingSeries}
                  >
                    {#if deletingSeries}<Spinner />{/if}
                    {deletingSeries ? 'Deleting…' : 'Delete series'}
                  </Button>
                {/snippet}
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Delete {detail.title}?</AlertDialogTitle>
                  <AlertDialogDescription>
                    This deletes every chapter and downloaded file for this series. This cannot be undone.
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>Cancel</AlertDialogCancel>
                  <AlertDialogAction variant="destructive" onclick={deleteSeriesNow}>Delete</AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          {/if}
        </div>

        {#if detail.authors.length}
          <p class="muted text-sm">
            By {#each detail.authors as a, i (a.id ?? a.name)}{#if i > 0}, {/if}<a
              class="text-inherit no-underline hover:text-brand-soft hover:underline"
              href={authorHref(a)}
              use:link
            >{a.name}</a>{/each}
          </p>
        {/if}
        {#if detail.description}
          <p class="my-3 max-h-28 overflow-y-auto text-sm text-text-dim [white-space:pre-line]">{detail.description}</p>
        {/if}
        {#if tagSections.length}
          <div class="mb-3 flex flex-col gap-1">
            {#each tagSections as section (section.label)}
              <div class="flex flex-wrap items-center gap-1.5">
                <span class="text-xs text-text-mute">{section.label}:</span>
                {#each section.tags as tag (tag.id)}
                  {@const sourceHref = genreSourceHref(tag)}
                  <DropdownMenu>
                    <DropdownMenuTrigger
                      class={cn(badgeVariants({ variant: 'secondary' }), 'cursor-pointer hover:text-brand-soft')}
                    >{tag.name}</DropdownMenuTrigger>
                    <DropdownMenuContent align="start">
                      <DropdownMenuItem onSelect={() => push(libraryHref(tag))}>View in Library</DropdownMenuItem>
                      {#if sourceHref}
                        <DropdownMenuItem onSelect={() => push(sourceHref)}>View on MangaDex</DropdownMenuItem>
                      {/if}
                    </DropdownMenuContent>
                  </DropdownMenu>
                {/each}
              </div>
            {/each}
          </div>
        {/if}
      </div>
    </div>

    <!-- Scanlation groups are a manga concept: a comic issue has exactly one canonical release, so
         there is nothing to rank. -->
    {#if isAdmin() && editMode && !isComic()}
      <Card class="mt-2 mb-5" size="sm">
        <CardContent>
          <Collapsible>
            <CollapsibleTrigger class="cursor-pointer text-sm text-text-dim">
              Preferred scanlation groups (highest first)
            </CollapsibleTrigger>
            <CollapsibleContent>
              <ol class="my-3 list-decimal pl-6">
                {#each preferred as g, i (g)}
                  <li class="flex items-center justify-between py-1">
                    <span>{g}</span>
                    <span class="flex gap-1">
                      <Button variant="secondary" size="mini" onclick={() => moveGroup(i, -1)} disabled={i === 0}>↑</Button>
                      <Button variant="secondary" size="mini" onclick={() => moveGroup(i, 1)} disabled={i === preferred.length - 1}>↓</Button>
                      <Button variant="secondary" size="mini" onclick={() => removeGroup(i)}>✕</Button>
                    </span>
                  </li>
                {/each}
              </ol>
              <div class="flex items-center gap-2.5">
                <Select type="single" bind:value={addGroupValue} onValueChange={addGroup}>
                  <SelectTrigger>{addGroupValue || 'Add group…'}</SelectTrigger>
                  <SelectContent>
                    {#each availableGroups.filter((g) => !preferred.includes(g)) as g}<SelectItem value={g} label={g}>{g}</SelectItem>{/each}
                  </SelectContent>
                </Select>
                <Button onclick={saveGroups}>Save order</Button>
              </div>

              <Separator class="my-3.5" />
              <div class="mt-1.5 flex flex-wrap items-center gap-3">
                <div class="flex items-center gap-1.5 text-sm text-text-dim">
                  <Checkbox id="policy-auto" bind:checked={policyAuto} />
                  <Label for="policy-auto">Series auto-download</Label>
                </div>
                <label class="flex items-center gap-1.5 text-sm text-text-dim">
                  Grace days <Input class="w-auto max-w-48" type="number" min="0" bind:value={policyGrace} />
                </label>
                <div class="flex items-center gap-1.5 text-sm text-text-dim">
                  Series languages
                  <MultiSelectDropdown label="Languages" options={languageOptions} bind:selected={policyLangs} />
                </div>
                <Button onclick={savePolicy}>Save policy</Button>
              </div>
            </CollapsibleContent>
          </Collapsible>
        </CardContent>
      </Card>
    {/if}

    <!-- Only matters for manually-imported content (whole-volume compilations mixed with
         individually-numbered extras) — gated on the same per-chapter canEdit signal the chapter-edit
         dialog uses, not isLocal/sourceId (misleading for e.g. a ComicVine-matched comic). -->
    {#if isAdmin() && editMode && hasManualChapters}
      <Card class="mt-2 mb-5" size="sm">
        <CardContent>
          <div class="flex flex-wrap items-center gap-3">
            <span class="text-sm text-text-dim">Chapter order</span>
            <Select type="single" bind:value={chapterSortMode}>
              <SelectTrigger>
                {chapterSortMode === 'VolumeThenChapter' ? 'Volume, then chapter' : 'Absolute chapter numbers'}
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="Absolute" label="Absolute chapter numbers">Absolute chapter numbers</SelectItem>
                <SelectItem value="VolumeThenChapter" label="Volume, then chapter">Volume, then chapter</SelectItem>
              </SelectContent>
            </Select>
            <Button onclick={saveSortMode}>Save</Button>
          </div>
          <p class="mt-1.5 text-xs text-text-mute">
            "Volume, then chapter" sorts whole-volume imports by volume number, with any
            individually-numbered extra tagged to a volume sorting right after it — for series
            mixing volume compilations with standalone specials.
          </p>
        </CardContent>
      </Card>
    {/if}

    <Card class="mt-2 mb-5">
      <CardHeader class="border-b border-border pb-(--card-spacing)">
        <CardTitle>{t('Chapters')}</CardTitle>
        <CardAction class="flex flex-col items-end gap-1.5">
          <div class="flex flex-wrap items-center justify-end gap-2.5">
            {#if !isComic()}
              <Select type="single" bind:value={lang} onValueChange={() => (groupFilter = null)}>
                <SelectTrigger class="w-auto">{lang ? languageName(lang) : lang}</SelectTrigger>
                <SelectContent>
                  {#each observedLanguages as l}<SelectItem value={l} label={languageName(l)}>{languageName(l)}</SelectItem>{/each}
                </SelectContent>
              </Select>
            {/if}
            <Tooltip>
              <TooltipTrigger>
                {#snippet child({ props })}
                  <!-- Wrapping span, not the button itself, is the trigger — a disabled <button>
                       never fires hover/focus events for the tooltip to key off. -->
                  <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
                  <span {...props} class="inline-flex" tabindex={canDownload ? -1 : 0}>
                    <Button size="sm" onclick={queueMissing} disabled={queuingMissing || !canDownload}>
                      {#if queuingMissing}<Spinner />{/if}
                      {queuingMissing ? 'Queuing…' : `Download missing${lang ? ` (${lang})` : ''}`}
                    </Button>
                  </span>
                {/snippet}
              </TooltipTrigger>
              <TooltipContent>
                {canDownload
                  ? "Queue downloads for every chapter you don't already have, in the selected language."
                  : "This series is manually downloaded — there's no source to queue downloads from."}
              </TooltipContent>
            </Tooltip>
            <Tooltip>
              <TooltipTrigger>
                {#snippet child({ props })}
                  <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
                  <span {...props} class="inline-flex" tabindex={canScan ? -1 : 0}>
                    <Button variant="secondary" size="sm" onclick={scanNow} disabled={scanning || !canScan}>
                      {#if scanning}<Spinner />{/if}
                      {scanning ? 'Scanning…' : 'Scan now'}
                    </Button>
                  </span>
                {/snippet}
              </TooltipTrigger>
              <TooltipContent>
                {canScan
                  ? `Check the source for new ${t('chapters')} right now, instead of waiting for the next scheduled scan.`
                  : `This series is manually imported — there's no source to scan for new ${t('chapters')}.`}
              </TooltipContent>
            </Tooltip>
            {#if isAdmin() && detail.sourceId && detail.sourceId !== 'local'}
              <Tooltip>
                <TooltipTrigger>
                  {#snippet child({ props })}
                    <Button {...props} variant="secondary" size="sm" onclick={refreshMetadataNow} disabled={refreshingMetadata}>
                      {#if refreshingMetadata}<Spinner />{/if}
                      {refreshingMetadata ? 'Refreshing…' : 'Refresh metadata'}
                    </Button>
                  {/snippet}
                </TooltipTrigger>
                <TooltipContent>
                  Re-fetch title, cover, description, and tags from {detail.sourceId} (doesn't touch chapters).
                </TooltipContent>
              </Tooltip>
            {/if}
          </div>
          {#if canDownload}
            <span class="muted text-xs">
              {detail.lastScannedAt ? `Scanned ${new Date(detail.lastScannedAt).toLocaleString()}` : 'Never scanned'}
            </span>
          {/if}
        </CardAction>
      </CardHeader>
      <CardContent>
        {#if groupStats.length > 0}
          <Collapsible class="mb-3">
            <CollapsibleTrigger class="cursor-pointer text-sm text-text-dim">
              Scanlation groups · {lang || 'all languages'} ({groupStats.length})
            </CollapsibleTrigger>
            <CollapsibleContent>
              <div class="mt-3 mb-1 flex flex-wrap gap-1.5">
                {#each groupStats as [g, count] (g)}
                  <button
                    class={cn(badgeVariants({ variant: groupFilter === g ? 'default' : 'secondary' }), 'cursor-pointer')}
                    onclick={() => (groupFilter = groupFilter === g ? null : g)}
                    title={`${count} chapter(s) by ${g}`}
                  >
                    {g}<span class="rounded-full bg-black/20 px-1.5 text-xs">{count}</span>
                  </button>
                {/each}
              </div>
              {#if groupFilter}
                <p class="muted text-sm">
                  Filtering to <strong>{groupFilter}</strong> —
                  <button
                    type="button"
                    class="cursor-pointer border-0 bg-transparent p-0 [font:inherit] text-brand-soft underline"
                    onclick={() => (groupFilter = null)}
                  >clear</button>
                </p>
              {/if}
            </CollapsibleContent>
          </Collapsible>
        {/if}

        <ul class="m-0 list-none overflow-hidden rounded-[var(--r-md)] border border-border p-0">
          {#each visibleChapters as c (c.id)}
            <li
              class="grid grid-cols-[11rem_1fr_auto_auto] items-center gap-4 border-b border-border-dim px-4 py-2 text-sm last:border-b-0 max-[640px]:grid-cols-[1fr_auto] max-[640px]:gap-y-1"
            >
              <span class="flex min-w-0 flex-col">
                <span class="font-semibold">{chapterLabel(c)}</span>
                {#if c.publishedAt}<span class="text-xs text-text-mute">{formatDate(c.publishedAt)}</span>{/if}
              </span>
              <span class="flex min-w-0 flex-col gap-0.5 text-text-dim max-[640px]:hidden">
                {#if c.title}<span class="truncate">{c.title}</span>{/if}
                {#if chapterGroups(c).length}
                  <span class="truncate text-xs text-text-mute" title={chapterGroups(c).join(', ')}>{chapterGroups(c).join(', ')}</span>
                {/if}
              </span>
              <span class="flex items-center justify-self-end gap-2 max-[640px]:col-start-2 max-[640px]:row-start-1">
                {#if progressText(c)}
                  <span class="flex items-center gap-1.5 text-sm text-brand-soft"><Spinner />{progressText(c)}</span>
                {:else if queuing[c.id]}
                  <span class="flex items-center gap-1.5 text-sm text-brand-soft"><Spinner />Queued…</span>
                {:else if c.downloaded}
                  <span class="text-sm text-ok">✓ {c.activeGroup ?? ''}</span>
                  {#if upgradeAvailable(c)}<span class="text-xs text-external">⇧ {upgradeAvailable(c)}</span>{/if}
                {:else if !hasDownloadable(c)}
                  <span class="text-xs text-text-mute">external only</span>
                {/if}
              </span>
              <span
                class="flex flex-wrap items-center justify-self-end gap-1.5 max-[640px]:col-span-2 max-[640px]:row-start-2 max-[640px]:w-full max-[640px]:justify-end"
              >
                {#if c.downloaded}
                  <Button variant="secondary" size="mini" href={`/read/${c.id}`} class="border-ok-border text-ok hover:border-ok">Read</Button>
                {/if}
                {#if hasDownloadable(c)}
                  {#if canDownload}
                    <Button size="mini" disabled={queuing[c.id]} onclick={() => download(c)}>
                      {#if queuing[c.id]}Queued…{:else if c.downloaded}{upgradeAvailable(c) ? 'Replace' : 'Re-download'}{:else}Download{/if}
                    </Button>
                  {:else}
                    <Tooltip>
                      <TooltipTrigger>
                        {#snippet child({ props })}
                          <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
                          <span {...props} class="inline-flex" tabindex={0}>
                            <Button size="mini" disabled>
                              {c.downloaded ? (upgradeAvailable(c) ? 'Replace' : 'Re-download') : 'Download'}
                            </Button>
                          </span>
                        {/snippet}
                      </TooltipTrigger>
                      <TooltipContent>
                        This series is manually downloaded — there's no source to download from.
                      </TooltipContent>
                    </Tooltip>
                  {/if}
                {/if}
                {#if isAdmin() && editMode && c.canEdit}
                  <EditChapterDialog chapter={c} allChapters={detail.chapters} onSaved={refresh} />
                {/if}
                {#if isAdmin() && editMode}
                  <AlertDialog>
                    <AlertDialogTrigger>
                      {#snippet child({ props })}
                        <Button
                          {...props}
                          variant="secondary"
                          size="mini"
                          class="border-danger-border text-destructive hover:border-destructive"
                          disabled={deletingChapter[c.id]}
                        >
                          {#if deletingChapter[c.id]}<Spinner />{/if}
                          {deletingChapter[c.id] ? 'Deleting…' : 'Delete'}
                        </Button>
                      {/snippet}
                    </AlertDialogTrigger>
                    <AlertDialogContent>
                      <AlertDialogHeader>
                        <AlertDialogTitle>
                          Delete {chapterLabel(c)}?
                        </AlertDialogTitle>
                        <AlertDialogDescription>
                          This deletes the downloaded file for this chapter. This cannot be undone.
                        </AlertDialogDescription>
                      </AlertDialogHeader>
                      <AlertDialogFooter>
                        <AlertDialogCancel>Cancel</AlertDialogCancel>
                        <AlertDialogAction variant="destructive" onclick={() => deleteChapterNow(c)}>Delete</AlertDialogAction>
                      </AlertDialogFooter>
                    </AlertDialogContent>
                  </AlertDialog>
                {/if}
              </span>
            </li>
          {/each}
        </ul>
      </CardContent>
    </Card>
  </section>
{/if}
