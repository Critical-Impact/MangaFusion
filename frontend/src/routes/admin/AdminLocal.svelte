<script lang="ts">
  import { onMount } from 'svelte'
  import { link } from 'svelte-spa-router'
  import {
    getLocalSeries,
    createLocalSeries,
    getInbox,
    getLibraryTagCatalog,
    importLocalFile,
    type LocalSeriesSummary,
    type InboxItem,
    type LocalChapterSpec,
  } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import { modeState } from '../../lib/mode.svelte'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Textarea } from '../../lib/components/ui/textarea/index.js'
  import { Input } from '../../lib/components/ui/input/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../../lib/components/ui/select/index.js'
  import { Spinner } from '../../lib/components/ui/spinner/index.js'

  let series = $state<LocalSeriesSummary[]>([])
  let inbox = $state<InboxItem[]>([])
  let tagCatalog = $state<string[]>([])
  let targetId = $state('')

  // Create-series form
  let title = $state('')
  let authors = $state('')
  let tags = $state('')
  let description = $state('')
  let rating = $state('Unknown')
  let status = $state('Unknown')
  let year = $state<number | null>(null)
  let origLang = $state('')
  let cover = $state('')
  let creating = $state(false)

  // Per-file import controls, keyed by inbox file name. Image imports use count/start (page-split);
  // prose imports use number/volume/title (one file = one chapter, no page split).
  let imp = $state<
    Record<string, { lang: string; count: number; start: string; volume: string; title: string }>
  >({})
  let busy = $state<Record<string, boolean>>({})

  const csv = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean)
  const ratings = ['Unknown', 'Safe', 'Suggestive', 'Erotica', 'Pornographic']
  const statuses = ['Unknown', 'Ongoing', 'Completed', 'Hiatus', 'Cancelled']

  // A light-novel library can hold BOTH prose and image chapters — the backend detects each file's actual
  // nature (a text EPUB/PDF vs a scanned one) and reports it per-file as `prose`, which drives the import
  // controls. So the library kind only decides which formats are offered; whether a given file uses prose
  // or page-split controls comes from the file itself, not the mode.
  const isLightNovel = $derived(modeState.kind === 'lightnovel')
  const importable = $derived(
    inbox.filter((i) =>
      isLightNovel
        ? ['cbz', 'cbr', 'epub', 'pdf', 'folder', 'txt', 'md'].includes(i.kind)
        : i.kind === 'cbz' || i.kind === 'cbr' || i.kind === 'epub' || i.kind === 'folder',
    ),
  )
  const covers = $derived(inbox.filter((i) => i.kind === 'image').map((i) => i.name))

  onMount(load)

  async function load() {
    try {
      ;[series, inbox] = await Promise.all([getLocalSeries(), getInbox()])
      if (!targetId && series.length) targetId = series[0].id
      for (const f of importable) imp[f.name] ??= { lang: 'en', count: 1, start: '1', volume: '', title: '' }
    } catch (err) {
      notify.error(msgOf(err))
    }
    try {
      tagCatalog = (await getLibraryTagCatalog()).map((t) => t.name)
    } catch {
      /* autocomplete is best-effort */
    }
  }

  async function create() {
    creating = true
    try {
      const { id } = await createLocalSeries({
        title: title.trim(),
        authors: csv(authors),
        tags: csv(tags),
        description: description || null,
        contentRating: rating,
        status,
        year,
        originalLanguage: origLang || null,
        coverFileName: cover || null,
      })
      notify.success(`Created “${title}”.`)
      title = authors = tags = description = origLang = cover = ''
      year = null
      rating = status = 'Unknown'
      series = await getLocalSeries()
      targetId = id
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      creating = false
    }
  }

  function buildSpecs(file: InboxItem, count: number, start: string): LocalChapterSpec[] {
    const n = Math.max(1, Math.floor(count))
    if (n === 1) return [{ number: start || '1', volume: null, title: null, pageCount: 0 }]
    const base = Math.floor(file.pageCount / n)
    const first = parseInt(start, 10) || 1
    const specs: LocalChapterSpec[] = []
    for (let i = 0; i < n; i++) {
      const pages = i === n - 1 ? file.pageCount - base * (n - 1) : base
      specs.push({ number: String(first + i), volume: null, title: null, pageCount: pages })
    }
    return specs
  }

  // Prose is one file = one chapter: a single spec carrying the hand-entered number/volume/title, no page
  // split. `start` doubles as the chapter number field in the prose control set.
  function buildProseSpec(cfg: { start: string; volume: string; title: string }): LocalChapterSpec[] {
    return [
      {
        number: cfg.start.trim() || '1',
        volume: cfg.volume.trim() || null,
        title: cfg.title.trim() || null,
        pageCount: 0,
      },
    ]
  }

  async function doImport(file: InboxItem) {
    if (!targetId) {
      notify.error('Pick a target series first.')
      return
    }
    const cfg = imp[file.name]
    busy[file.name] = true
    try {
      const specs = file.prose ? buildProseSpec(cfg) : buildSpecs(file, cfg.count, cfg.start)
      const { imported } = await importLocalFile(targetId, file.name, cfg.lang.trim(), specs)
      notify.success(`Imported ${imported} chapter(s) from ${file.name}.`)
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      busy[file.name] = false
    }
  }

  const msgOf = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')
</script>

<div class="flex flex-col gap-6">
  <section>
    <h3 class="mb-[0.7rem] text-base">Create a local series</h3>
    <div class="flex max-w-[560px] flex-col gap-[0.6rem]">
      <Input placeholder="Title *" bind:value={title} />
      <Input placeholder="Authors (comma-separated)" bind:value={authors} />
      <Input placeholder="Tags (comma-separated)" bind:value={tags} list="tag-catalog" />
      <datalist id="tag-catalog">
        {#each tagCatalog as t (t)}<option value={t}></option>{/each}
      </datalist>
      <Textarea placeholder="Description" bind:value={description} class="min-h-[3.5rem] resize-y" />
      <div class="flex flex-wrap gap-[0.8rem]">
        <label class="flex flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
          Rating
          <Select type="single" bind:value={rating}>
            <SelectTrigger>{rating}</SelectTrigger>
            <SelectContent>
              {#each ratings as r}<SelectItem value={r} label={r}>{r}</SelectItem>{/each}
            </SelectContent>
          </Select>
        </label>
        <label class="flex flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
          Status
          <Select type="single" bind:value={status}>
            <SelectTrigger>{status}</SelectTrigger>
            <SelectContent>
              {#each statuses as s}<SelectItem value={s} label={s}>{s}</SelectItem>{/each}
            </SelectContent>
          </Select>
        </label>
        <label class="flex flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
          Year <Input class="max-w-[6rem]" type="number" bind:value={year} />
        </label>
        <label class="flex flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
          Orig. lang <Input class="max-w-[6rem]" placeholder="ja" bind:value={origLang} />
        </label>
      </div>
      {#if covers.length}
        <label class="flex flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
          Cover
          <Select type="single" bind:value={cover}>
            <SelectTrigger>{cover || '— none —'}</SelectTrigger>
            <SelectContent>
              <SelectItem value="" label="— none —">— none —</SelectItem>
              {#each covers as c}<SelectItem value={c} label={c}>{c}</SelectItem>{/each}
            </SelectContent>
          </Select>
        </label>
      {/if}
      <Button onclick={create} disabled={creating || !title.trim()}>
        {#if creating}<Spinner />{/if}
        {creating ? 'Creating…' : 'Create series'}
      </Button>
    </div>
  </section>

  <section>
    <h3 class="mb-[0.7rem] text-base">Import files from the inbox</h3>
    <label class="mb-2 flex max-w-[360px] flex-col gap-[0.25rem] text-[0.8rem] text-text-dim">
      Target series
      <Select type="single" bind:value={targetId}>
        <SelectTrigger>{series.find((s) => s.id === targetId)?.title ?? '— pick a local series —'}</SelectTrigger>
        <SelectContent>
          <SelectItem value="" label="— pick a local series —">— pick a local series —</SelectItem>
          {#each series as s (s.id)}<SelectItem value={s.id} label={s.title}>{s.title}</SelectItem>{/each}
        </SelectContent>
      </Select>
    </label>
    {#if targetId}
      <a class="ml-[0.6rem] text-[0.8rem] text-brand-soft no-underline" href={`/library/${targetId}`} use:link>Open this series ↗</a>
    {/if}

    {#if importable.length === 0}
      {#if isLightNovel}
        <p class="muted">No importable files in the inbox. Drop <code>.epub</code>, <code>.pdf</code>, <code>.txt</code>, or <code>.md</code> files (text or scanned) into the configured inbox path.</p>
      {:else}
        <p class="muted">No importable files in the inbox. Drop <code>.cbz</code>/<code>.cbr</code> files, image-based <code>.epub</code> files, or image folders into the configured inbox path.</p>
      {/if}
    {:else}
      <ul class="mt-[0.6rem] list-none overflow-hidden rounded-[var(--r-md)] border border-border p-0">
        {#each importable as f (f.name)}
          <li class="flex flex-wrap items-center justify-between gap-4 border-b border-border-dim px-4 py-[0.6rem] last:border-b-0">
            <span class="flex flex-col text-[0.88rem]">
              {f.name}<span class="text-[0.72rem] text-text-mute">{f.kind}{f.prose ? ' · whole volume' : ` · ${f.pageCount} pages`}</span>
            </span>
            <span class="flex items-center gap-2">
              <Input class="w-[4.5rem]" bind:value={imp[f.name].lang} title="language" />
              {#if f.prose}
                <label class="flex items-center gap-[0.3rem] text-[0.75rem] text-text-mute">
                  # <Input class="w-[4rem]" bind:value={imp[f.name].start} title="chapter number" />
                </label>
                <label class="flex items-center gap-[0.3rem] text-[0.75rem] text-text-mute">
                  vol <Input class="w-[4rem]" bind:value={imp[f.name].volume} title="volume (optional)" />
                </label>
                <Input class="w-[9rem]" placeholder="title (optional)" bind:value={imp[f.name].title} />
              {:else}
                <label class="flex items-center gap-[0.3rem] text-[0.75rem] text-text-mute">
                  chapters <Input class="w-[4.5rem]" type="number" min="1" bind:value={imp[f.name].count} />
                </label>
                <label class="flex items-center gap-[0.3rem] text-[0.75rem] text-text-mute">
                  from # <Input class="w-[4.5rem]" bind:value={imp[f.name].start} />
                </label>
              {/if}
              <Button variant="secondary" size="mini" disabled={busy[f.name] || !targetId} onclick={() => doImport(f)}>
                {#if busy[f.name]}<Spinner />{/if}
                Import
              </Button>
            </span>
          </li>
        {/each}
      </ul>
    {/if}
  </section>
</div>
