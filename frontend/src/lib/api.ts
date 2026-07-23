// Thin client over the ASP.NET Identity + app endpoints. Auth is cookie-based and same-origin.
//
// Every library-scoped request carries the active media kind (?kind=manga|comic). That's read straight
// from the mode store rather than passed in by each caller — the manga and comic libraries share a
// database, so a request that forgets its kind doesn't 400, it silently returns the *other* library's
// content. Making it impossible to forget is worth the module-level coupling.

import { currentKind, homeKind } from './mode.svelte'

/** The Home rails are the one place a user can opt into a combined view across both libraries, so they
 *  omit ?kind= entirely rather than always sending one. Everything else is always scoped. */
function homeKindParam(): string {
  const kind = homeKind()
  return kind ? `&kind=${kind}` : ''
}

export interface Me {
  id: string
  email: string
  roles: string[]
  theme: string | null
  defaultLanguage: string | null
  /** "manga" | "comic" — which library the SPA should open on. Null until the user has picked one. */
  preferredKind: string | null
  /** When true, the Home rails span both libraries instead of the one you're in. */
  homeAcrossLibraries: boolean
  /** The user's saved Home dashboard layout, or null to use the default rail set. */
  dashboardLayout: DashboardItem[] | null
}

/** One entry in the Home dashboard layout: a built-in rail (by fixed id) or a collection (by GUID),
 * with a visibility flag. Order in the array is the display order. */
export interface DashboardItem {
  type: 'rail' | 'collection'
  key: string
  visible: boolean
}

export interface LanguageOption {
  code: string
  name: string
}

export interface SourceSummary {
  id: string
  displayName: string
  capabilities: string[]
  requiresAuth: boolean
  configured: boolean
}

export interface CredentialField {
  name: string
  label: string
  secret: boolean
}

/** An author/artist credit. `sourceId`/`id` are null when the source has no per-author identity
 * (e.g. local/manual imports) — link to the author page with the "local" fallback route in that case. */
export interface AuthorRef {
  sourceId: string | null
  id: string | null
  name: string
}

/** A tag attached to a source-browsed series. `id` is null when the source exposes no per-tag id. */
export interface SeriesTag {
  id: string | null
  name: string
  group: string
}

export interface Series {
  sourceId: string
  sourceSeriesId: string
  title: string
  altTitles: string[]
  description: string | null
  coverUrl: string | null
  authors: AuthorRef[]
  artists: AuthorRef[]
  tags: SeriesTag[]
  contentRating: string
  status: string
  year: number | null
  originalLanguage: string | null
  availableTranslatedLanguages: string[]
  lastChapter: string | null
  siteUrl: string | null
}

export interface Chapter {
  sourceId: string
  sourceChapterId: string
  volume: string | null
  number: string | null
  title: string | null
  language: string
  scanlationGroups: string[]
  pageCount: number | null
  publishedAt: string | null
  isExternal: boolean
  externalUrl: string | null
}

export interface Paged<T> {
  items: T[]
  total: number
  limit: number
  offset: number
}

const jsonHeaders = { 'Content-Type': 'application/json' }

async function getJson<T>(url: string): Promise<T> {
  const res = await fetch(url, { credentials: 'include' })
  if (!res.ok) throw new Error(await extractError(res))
  return (await res.json()) as T
}

// --- Version ---------------------------------------------------------------------------------

export interface AppInfo {
  version: string
  /** True in the standalone Windows/Linux build, false under Docker/Kubernetes — gates the
   *  in-app shutdown/restart menu and is meaningless anywhere else. */
  desktopMode: boolean
}

export async function getAppInfo(): Promise<AppInfo> {
  const res = await fetch('/api/version')
  if (!res.ok) return { version: 'unknown', desktopMode: false }
  return (await res.json()) as AppInfo
}

// --- Auth / session --------------------------------------------------------------------------

export async function getMe(): Promise<Me | null> {
  const res = await fetch('/api/me', { credentials: 'include' })
  if (res.status === 401) return null
  if (!res.ok) throw new Error(`Failed to load session (${res.status})`)
  return (await res.json()) as Me
}

export async function login(email: string, password: string): Promise<void> {
  const res = await fetch('/api/auth/login?useCookies=true', {
    method: 'POST',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) throw new Error('Invalid email or password.')
}

export async function register(email: string, password: string): Promise<void> {
  const res = await fetch('/api/auth/register', {
    method: 'POST',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ email, password }),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export async function logout(): Promise<void> {
  await fetch('/api/auth/logout', {
    method: 'POST',
    headers: jsonHeaders,
    credentials: 'include',
    body: '{}',
  })
}

export async function setUserTheme(theme: string): Promise<void> {
  const res = await fetch('/api/me/theme', {
    method: 'PUT',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ theme }),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export async function setUserDefaultLanguage(language: string | null): Promise<void> {
  const res = await fetch('/api/me/language', {
    method: 'PUT',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ language }),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export async function changeEmail(email: string): Promise<void> {
  const res = await fetch('/api/me/email', {
    method: 'PUT',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ email }),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  const res = await fetch('/api/me/password', {
    method: 'PUT',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify({ currentPassword, newPassword }),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export const getLanguages = () => getJson<LanguageOption[]>('/api/languages')

// --- Sources ---------------------------------------------------------------------------------

/** Omit `kind` for every registered source (the admin credentials screen); pass one to get only the
 * sources that serve that library — this is what backs the browse/search source pickers. */
export const getSources = (kind?: string) =>
  getJson<SourceSummary[]>(kind ? `/api/sources?kind=${kind}` : '/api/sources')

export const getCredentialFields = (sourceId: string) =>
  getJson<CredentialField[]>(`/api/sources/${sourceId}/credentials/fields`)

export async function setCredentials(sourceId: string, values: Record<string, string>): Promise<void> {
  const res = await fetch(`/api/sources/${sourceId}/credentials`, {
    method: 'PUT',
    headers: jsonHeaders,
    credentials: 'include',
    body: JSON.stringify(values),
  })
  if (!res.ok) throw new Error(await extractError(res))
}

export async function testCredentials(sourceId: string): Promise<boolean> {
  const res = await fetch(`/api/sources/${sourceId}/credentials/test`, {
    method: 'POST',
    headers: jsonHeaders,
    credentials: 'include',
    body: '{}',
  })
  if (!res.ok) throw new Error(await extractError(res))
  const body = (await res.json()) as { success: boolean }
  return body.success
}

export interface SourceTag {
  id: string
  name: string
  group: string
}

export const getTags = (sourceId: string) => getJson<SourceTag[]>(`/api/sources/${sourceId}/tags`)

export function searchSeries(
  sourceId: string,
  q: string,
  opts: {
    limit?: number
    offset?: number
    lang?: string[]
    tag?: string[]
    rating?: string
    order?: string
    authorId?: string
    /** Only used by the aggregate 'all' source: which library's sources to fan out to. */
    kind?: string
  } = {},
): Promise<Paged<Series>> {
  const params = new URLSearchParams()
  if (q) params.set('q', q)
  if (opts.limit) params.set('limit', String(opts.limit))
  if (opts.offset) params.set('offset', String(opts.offset))
  for (const l of opts.lang ?? []) params.append('lang', l)
  for (const t of opts.tag ?? []) params.append('tag', t)
  if (opts.rating) params.append('rating', opts.rating)
  if (opts.order) params.set('order', opts.order)
  if (opts.authorId) params.set('authorId', opts.authorId)
  if (opts.kind) params.set('kind', opts.kind)
  return getJson<Paged<Series>>(`/api/sources/${sourceId}/search?${params}`)
}

export const getSeries = (sourceId: string, seriesId: string) =>
  getJson<Series>(`/api/sources/${sourceId}/series/${seriesId}`)

export function getChapters(
  sourceId: string,
  seriesId: string,
  opts: { lang?: string[]; order?: 'asc' | 'desc'; limit?: number; includeExternal?: boolean } = {},
): Promise<Paged<Chapter>> {
  const params = new URLSearchParams()
  for (const l of opts.lang ?? []) params.append('lang', l)
  if (opts.order) params.set('order', opts.order)
  if (opts.limit) params.set('limit', String(opts.limit))
  if (opts.includeExternal) params.set('includeExternal', 'true')
  return getJson<Paged<Chapter>>(`/api/sources/${sourceId}/series/${seriesId}/chapters?${params}`)
}

// --- Library ---------------------------------------------------------------------------------

export interface LibrarySeries {
  id: string
  title: string
  coverUrl: string | null
  followed: boolean
  tags: string[]
  year: number | null
  addedAt: string
  chapterCount: number
  /** Every source this series carries a link for (e.g. "mangadex", "mangaupdates", "local"). */
  sources: string[]
}

export interface LibraryRelease {
  id: string
  groups: string[]
  groupKey: string | null
  isExternal: boolean
  publishedAt: string | null
  pageCount: number | null
}

export interface LibraryChapter {
  id: string
  language: string
  number: string | null
  numberSort: number | null
  volume: string | null
  volumeSort: number | null
  title: string | null
  downloaded: boolean
  activeGroup: string | null
  pageIndex: number
  completed: boolean
  publishedAt: string | null
  releases: LibraryRelease[]
  /** True if this chapter's active release was manually imported (local library / import wizard),
   *  making its number/volume/title editable. False for chapters sourced from a live download source. */
  canEdit: boolean
}

export interface LibrarySeriesDetail {
  id: string
  title: string
  altTitles: string[]
  description: string | null
  coverUrl: string | null
  authors: AuthorRef[]
  tags: TagInfo[]
  contentRating: string
  status: string
  year: number | null
  preferredGroups: string[]
  autoDownload: boolean
  gracePeriodDays: number | null
  seriesLanguages: string[]
  lastScannedAt: string | null
  sourceId: string | null
  sourceName: string | null
  sourceSeriesId: string | null
  siteUrl: string | null
  followed: boolean
  followAutoDownload: boolean
  followLanguages: string[]
  reading: boolean
  chapters: LibraryChapter[]
  /** "Absolute" (default — sorts purely by chapter number) or "VolumeThenChapter" (sorts by volume
   *  first, chapter number second within it — for manually-imported series mixing whole-volume
   *  compilations with individually-numbered extras tagged to a specific volume). */
  sortMode: string
  /** True once an admin has manually edited that field — it's excluded from future metadata
   *  refreshes/monitor scans until unlocked. */
  titleLocked: boolean
  yearLocked: boolean
  descriptionLocked: boolean
  coverLocked: boolean
}

/** Route to the author page for a credit. Falls back to a name-based "local" route when the source
 * exposes no per-author id (matched by name server-side — see LibraryQuery.AuthorSourceId). */
export function authorHref(a: AuthorRef): string {
  const sourceId = a.sourceId ?? 'local'
  const authorId = a.id ?? a.name
  return `/author/${encodeURIComponent(sourceId)}/${encodeURIComponent(authorId)}?name=${encodeURIComponent(a.name)}`
}

/** The source's public web page for a series, or null if unknown for that source. */
export function sourceSeriesUrl(sourceId: string | null, sourceSeriesId: string | null): string | null {
  if (!sourceId || !sourceSeriesId) return null
  if (sourceId === 'mangadex') return `https://mangadex.org/title/${sourceSeriesId}`
  return null
}

export interface AppNotification {
  id: string
  title: string
  body: string | null
  seriesId: string | null
  createdAt: string
  read: boolean
  severity: 'Info' | 'Warning' | 'Error'
}

export interface DownloadItem {
  id: string
  seriesId: string
  chapterId: string | null
  description: string | null
  status: string
  pagesDone: number
  pagesTotal: number
  error: string | null
  createdAt: string
}

async function send<T>(url: string, method: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: jsonHeaders,
    credentials: 'include',
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (!res.ok) throw new Error(await extractError(res))
  return (res.status === 204 ? undefined : await res.json()) as T
}

export const addToLibrary = (sourceId: string, sourceSeriesId: string) =>
  send<{ id: string }>('/api/library/series', 'POST', { sourceId, sourceSeriesId })

export interface SeriesRef {
  sourceId: string
  sourceSeriesId: string
}

export interface LibraryMembership extends SeriesRef {
  libraryId: string
}

/** Which of the given source series are already in the library, with the library id to link to.
 * Refs absent from the result aren't in the library. */
export const getLibraryMembership = (refs: SeriesRef[]) =>
  send<LibraryMembership[]>('/api/library/series/membership', 'POST', { refs })

export interface TagInfo {
  id: string
  name: string
  group: string
  sourceId: string | null
  sourceTagId: string | null
}

/** Route to the source's own genre/theme search for a tag (via the `/genre/:sourceId/:tagId` route),
 * or null if the tag has no known source (e.g. locally-created). */
export function genreSourceHref(tag: TagInfo): string | null {
  if (!tag.sourceId || !tag.sourceTagId) return null
  return `/genre/${encodeURIComponent(tag.sourceId)}/${encodeURIComponent(tag.sourceTagId)}`
}

/** Same as {@link genreSourceHref}, for a tag attached to a source-browsed {@link Series} (whose
 * source is the series' own `sourceId` rather than carried on the tag itself). */
export function seriesTagHref(sourceId: string, tag: SeriesTag): string | null {
  if (!tag.id) return null
  return `/genre/${encodeURIComponent(sourceId)}/${encodeURIComponent(tag.id)}`
}

export function getLibrary(
  opts: {
    q?: string
    /** One entry per facet: OR within a facet, AND across facets. Which groups they came from is the
     *  caller's business — the server filters on tag ids alone. */
    tagFacets?: string[][]
    rating?: string
    sort?: string
    order?: 'asc' | 'desc'
    limit?: number
    offset?: number
    authorSourceId?: string
    authorId?: string
    sourceId?: string
  } = {},
): Promise<Paged<LibrarySeries>> {
  const params = new URLSearchParams({ kind: currentKind() })
  if (opts.q) params.set('q', opts.q)
  for (const facet of opts.tagFacets ?? []) {
    if (facet.length > 0) params.append('tags', facet.join(','))
  }
  if (opts.rating) params.set('rating', opts.rating)
  if (opts.sort) params.set('sort', opts.sort)
  if (opts.order) params.set('order', opts.order)
  if (opts.limit) params.set('limit', String(opts.limit))
  if (opts.offset) params.set('offset', String(opts.offset))
  if (opts.authorSourceId) params.set('authorSourceId', opts.authorSourceId)
  if (opts.authorId) params.set('authorId', opts.authorId)
  if (opts.sourceId) params.set('sourceId', opts.sourceId)
  return getJson<Paged<LibrarySeries>>(`/api/library/series?${params}`)
}

/** Tags actually in use across the library, optionally restricted to one group — for filter dropdowns. */
export function getLibraryTags(group?: string): Promise<TagInfo[]> {
  const params = new URLSearchParams({ kind: currentKind() })
  if (group) params.set('group', group)
  return getJson<TagInfo[]>(`/api/library/tags?${params}`)
}

/** Every known tag regardless of usage — feeds the local-import tag picker. */
export const getLibraryTagCatalog = () =>
  getJson<TagInfo[]>(`/api/library/tags/catalog?kind=${currentKind()}`)

export const getLibraryTitles = () => getJson<{ id: string; title: string }[]>('/api/library/series/titles')

export const getLibrarySeries = (id: string) =>
  getJson<LibrarySeriesDetail>(`/api/library/series/${id}`)

export const downloadChapter = (chapterId: string, releaseId?: string) =>
  send<{ downloadId: string }>(`/api/library/chapters/${chapterId}/download`, 'POST', { releaseId })

export const downloadMissing = (seriesId: string, languages: string[]) =>
  send<{ queued: number }>(`/api/library/series/${seriesId}/download-missing`, 'POST', { languages })

export const getDownloads = () => getJson<DownloadItem[]>('/api/library/downloads')

export const followSeries = (seriesId: string, languages: string[], autoDownload: boolean) =>
  send<unknown>(`/api/library/series/${seriesId}/follow`, 'POST', { languages, autoDownload })

export const unfollowSeries = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/follow`, 'DELETE')

export const setPreferredGroups = (seriesId: string, groups: string[]) =>
  send<void>(`/api/library/series/${seriesId}/groups`, 'PUT', { groups })

export const setPolicy = (
  seriesId: string,
  gracePeriodDays: number | null,
  autoDownload: boolean,
  languages: string[],
) => send<void>(`/api/library/series/${seriesId}/policy`, 'PUT', { gracePeriodDays, autoDownload, languages })

/** Switches a series' chapter ordering mode ("Absolute" | "VolumeThenChapter"), recomputing every
 *  existing chapter's sort key. Rejects (400) if the switch would merge two chapters onto the same
 *  identity — give the colliding chapters distinct numbers/volumes first. */
export const setChapterSortMode = (seriesId: string, sortMode: string) =>
  send<void>(`/api/library/series/${seriesId}/sort-mode`, 'PUT', { sortMode })

export const scanSeries = (seriesId: string) =>
  send<unknown>(`/api/library/series/${seriesId}/scan`, 'POST')

/** Re-fetches a series' metadata from its metadata-primary source (e.g. re-pulling MangaUpdates data
 * after import). Fails if the series has no external metadata source (a "local"-only series). */
export const refreshSeriesMetadata = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/refresh-metadata`, 'POST', {})

/** Permanently deletes a series and every chapter/artifact/file inside it. Cannot be undone. */
export const deleteSeries = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}`, 'DELETE')

/** Permanently deletes one chapter. If it was the sole chapter backed by its artifact file, the file
 * is deleted too; if the artifact is shared with other chapters (a multi-chapter volume), it's left
 * in place for them. Cannot be undone. */
export const deleteChapter = (chapterId: string) =>
  send<void>(`/api/library/chapters/${chapterId}`, 'DELETE')

/** Edits a manually-imported chapter's number/volume/title, recomputing its sort position. Rejects
 *  (400) if the chapter isn't manually imported, or if the new number/volume collides with a sibling
 *  chapter's — the caller should surface the thrown Error's message. */
export const updateChapter = (
  chapterId: string,
  body: { number: string | null; volume: string | null; title: string | null },
) => send<void>(`/api/library/chapters/${chapterId}`, 'PATCH', body)

/** Manually sets a series' title/year/description, locking all three against being overwritten by a
 *  future metadata refresh or monitor scan until {@link unlockSeriesMetadata} is called. */
export const updateSeriesMetadata = (
  seriesId: string,
  body: { title: string; year: number | null; description: string | null },
) => send<void>(`/api/library/series/${seriesId}`, 'PATCH', body)

/** Clears the title/year/description lock — the next metadata refresh/monitor scan overwrites them
 *  from the source again. */
export const unlockSeriesMetadata = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/metadata-lock`, 'DELETE')

/** Uploads a custom series cover, locking it against being overwritten by a future metadata refresh.
 *  Multipart, so it bypasses `send()`/jsonHeaders — the browser sets the multipart boundary itself,
 *  which it can't do if we force a Content-Type (mirrors uploadCollectionCover). */
export async function uploadSeriesCover(seriesId: string, file: File): Promise<void> {
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(`/api/library/series/${seriesId}/cover`, { method: 'POST', credentials: 'include', body: form })
  if (!res.ok) throw new Error(await extractError(res))
}

/** Clears the cover lock — the next metadata refresh re-downloads the source's cover again. */
export const unlockSeriesCover = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/cover-lock`, 'DELETE')

// --- Reader -----------------------------------------------------------------------------------

export interface ChapterManifest {
  chapterId: string
  artifactId: string
  pageCount: number
  startPageIndex: number
  readingDirection: 'ltr' | 'rtl'
  seriesId: string
  seriesTitle: string
  number: string | null
  volume: string | null
  language: string
}

export interface ReaderNeighbors {
  prevChapterId: string | null
  nextChapterId: string | null
}

export interface ContinueReadingItem {
  seriesId: string
  seriesTitle: string
  coverUrl: string | null
  chapterId: string
  number: string | null
  volume: string | null
  language: string
  pageIndex: number
  pageCount: number
  updatedAt: string
}

export const getChapterManifest = (chapterId: string) =>
  getJson<ChapterManifest>(`/api/library/chapters/${chapterId}/manifest`)

/** URL for a single page image — used directly as an <img src> so the browser caches/preloads it. */
export const pageUrl = (chapterId: string, index: number) =>
  `/api/library/chapters/${chapterId}/pages/${index}`

export const saveProgress = (chapterId: string, pageIndex: number, completed: boolean) =>
  send<void>(`/api/library/chapters/${chapterId}/progress`, 'PUT', { pageIndex, completed })

export const getNeighbors = (chapterId: string) =>
  getJson<ReaderNeighbors>(`/api/library/chapters/${chapterId}/neighbors`)

// --- Preview reader (read live from a source, no library / no progress) -----------------------

export interface SourceChapterManifest {
  sourceId: string
  sourceChapterId: string
  pageCount: number
  readingDirection: 'ltr' | 'rtl'
}

export const getPreviewManifest = (sourceId: string, chapterId: string) =>
  getJson<SourceChapterManifest>(`/api/sources/${sourceId}/chapters/${chapterId}/manifest`)

/** URL for a single source page image — proxied server-side so required headers are applied. */
export const previewPageUrl = (sourceId: string, chapterId: string, index: number) =>
  `/api/sources/${sourceId}/chapters/${chapterId}/pages/${index}`

export const getContinueReading = (limit = 12) =>
  getJson<ContinueReadingItem[]>(`/api/library/continue-reading?limit=${limit}${homeKindParam()}`)

export interface RecentDownloadItem {
  seriesId: string
  seriesTitle: string
  coverUrl: string | null
  chapterId: string
  number: string | null
  volume: string | null
  downloadedAt: string
}

export const getRecentDownloads = (limit = 12) =>
  getJson<RecentDownloadItem[]>(`/api/library/recent-downloads?limit=${limit}${homeKindParam()}`)

export interface RecentlyUpdatedItem {
  seriesId: string
  seriesTitle: string
  coverUrl: string | null
  chapterId: string | null
  number: string | null
  volume: string | null
  updatedAt: string
}

export const getRecentlyUpdated = (limit = 12) =>
  getJson<RecentlyUpdatedItem[]>(`/api/library/recently-updated?limit=${limit}${homeKindParam()}`)

export const addReading = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/reading`, 'POST', {})

export const dismissReading = (seriesId: string) =>
  send<void>(`/api/library/series/${seriesId}/reading`, 'DELETE')

// --- Admin -----------------------------------------------------------------------------------

export interface AdminSettings {
  monitorCron: string
  defaultLanguages: string[]
  defaultGraceDays: number
  allowSelfRegistration: boolean
  /** Null/empty means "use the quiet default" — EF Core/HttpClient/Hangfire noise stays suppressed. */
  minimumLogLevel: string | null
}

export interface AdminUser {
  id: string
  email: string | null
  roles: string[]
  disabled: boolean
}

export const getAdminSettings = () => getJson<AdminSettings>('/api/admin/settings')

export const putAdminSettings = (patch: Partial<AdminSettings>) =>
  send<AdminSettings>('/api/admin/settings', 'PUT', patch)

/** Desktop-mode only — the server rejects both under Docker/Kubernetes. */
export const shutdownServer = () => send<void>('/api/admin/system/shutdown', 'POST', {})
export const restartServer = () => send<void>('/api/admin/system/restart', 'POST', {})

export interface BackgroundStats {
  enqueued: number
  processing: number
  succeeded: number
  failed: number
  scheduled: number
  servers: number
}

export interface TaskFeedItem {
  id: string
  kind: 'download' | 'series-scan' | 'library-scan'
  target: string
  seriesId: string | null
  state: string
  pagesDone: number | null
  pagesTotal: number | null
  error: string | null
  hangfireJobId: string | null
  createdAt: string | null
  startedAt: string | null
  finishedAt: string | null
}

export interface TaskFeed {
  stats: BackgroundStats
  items: TaskFeedItem[]
}

export const getTasks = (limit = 100) => getJson<TaskFeed>(`/api/admin/tasks?limit=${limit}`)

export const retryDownloadTask = (downloadId: string) =>
  send<{ downloadId: string }>(`/api/admin/tasks/${downloadId}/retry`, 'POST', {})

export const requeueJob = (jobId: string) =>
  send<void>(`/api/admin/tasks/hangfire/${encodeURIComponent(jobId)}/requeue`, 'POST', {})

export const deleteJob = (jobId: string) =>
  send<void>(`/api/admin/tasks/hangfire/${encodeURIComponent(jobId)}`, 'DELETE')

export interface LocalSeriesMetadata {
  title: string
  altTitles?: string[]
  authors?: string[]
  tags?: string[]
  description?: string | null
  contentRating?: string | null
  status?: string | null
  year?: number | null
  originalLanguage?: string | null
  coverFileName?: string | null
}

export interface LocalSeriesSummary {
  id: string
  title: string
}

export interface InboxItem {
  name: string
  kind: string
  pageCount: number
  sizeBytes: number
}

export interface LocalChapterSpec {
  number: string | null
  volume: string | null
  title: string | null
  pageCount: number
}

// The local tool is per-library: the inbox is split on disk, the import-target picker must only offer
// series from the library you're in, and — the bug this fixes — a created series took the server-side
// enum default and became a Manga no matter which mode you were in.
export const getLocalSeries = () =>
  getJson<LocalSeriesSummary[]>(`/api/local/series?kind=${currentKind()}`)

export const createLocalSeries = (metadata: LocalSeriesMetadata) =>
  send<{ id: string }>(`/api/local/series?kind=${currentKind()}`, 'POST', metadata)

export const getInbox = () => getJson<InboxItem[]>(`/api/local/inbox?kind=${currentKind()}`)

export const importLocalFile = (seriesId: string, fileName: string, language: string, chapters: LocalChapterSpec[]) =>
  send<{ imported: number }>(`/api/local/series/${seriesId}/import`, 'POST', { fileName, language, chapters })

export const getUsers = () => getJson<AdminUser[]>('/api/admin/users')

export const createUser = (email: string, password: string, roles: string[]) =>
  send<AdminUser>('/api/admin/users', 'POST', { email, password, roles })

export const setUserRoles = (id: string, roles: string[]) =>
  send<AdminUser>(`/api/admin/users/${id}/roles`, 'POST', { roles })

export const disableUser = (id: string) => send<void>(`/api/admin/users/${id}/disable`, 'POST', {})

export const enableUser = (id: string) => send<void>(`/api/admin/users/${id}/enable`, 'POST', {})

export const deleteUser = (id: string) => send<void>(`/api/admin/users/${id}`, 'DELETE')

// --- Migration tool ----------------------------------------------------------------------------

export interface MigrationItemDetail {
  id: string
  fileName: string
  uuidPrefix: string | null
  number: string | null
  chapterTitle: string | null
  pageCount: number
  sizeBytes: number
  matchedGroup: string | null
  disposition: string
  isWinner: boolean
  flagReason: string | null
}

export interface MigrationSeriesDetail {
  id: string
  folderName: string
  comicInfoSeriesTitle: string | null
  matchedSourceSeriesId: string | null
  matchedTitle: string | null
  regime: string
  confidence: number
  status: string
  conflictReason: string | null
  hasRankingOnlyConflict: boolean
  existingLibrarySeriesId: string | null
  committedLibrarySeriesId: string | null
  groupRanking: string[]
  items: MigrationItemDetail[]
  commitItemsDone: number | null
  commitItemsTotal: number | null
  /** True when a commit is (was) running for this series but its Hangfire job is no longer actually
   *  alive — e.g. the app restarted mid-commit — so nothing is coming back to finish it. */
  commitJobCrashed: boolean
}

export interface MigrationBatchDetail {
  id: string
  createdAt: string
  status: string
  error: string | null
  divertedFolders: string[]
  series: MigrationSeriesDetail[]
  commitSeriesDone: number | null
  commitSeriesTotal: number | null
  /** Same crash detection as MigrationSeriesDetail.commitJobCrashed, for the bulk "commit all clean" job. */
  commitJobCrashed: boolean
}

export interface MigrationBatchSummary {
  id: string
  createdAt: string
  status: string
  seriesCount: number
  error: string | null
}

export const startMigrationScan = () => send<{ batchId: string }>('/api/migration/scan', 'POST', {})

export const getMigrationBatches = () => getJson<MigrationBatchSummary[]>('/api/migration/batches')

export const getMigrationBatch = (id: string) => getJson<MigrationBatchDetail>(`/api/migration/batches/${id}`)

export const setMigrationSeriesMatch = (seriesId: string, sourceSeriesId: string | null) =>
  send<void>(`/api/migration/series/${seriesId}/match`, 'PATCH', { sourceSeriesId })

export const setMigrationMergeTarget = (seriesId: string, existingLibrarySeriesId: string | null) =>
  send<void>(`/api/migration/series/${seriesId}/merge-target`, 'PATCH', { existingLibrarySeriesId })

export const setMigrationItemDisposition = (itemId: string, disposition: string) =>
  send<void>(`/api/migration/items/${itemId}`, 'PATCH', { disposition })

// Both commit endpoints enqueue a background job and return immediately (204); the batch moves to
// 'Committing' and the caller polls it for completion.
export const commitMigrationSeries = (seriesId: string) =>
  send<void>(`/api/migration/series/${seriesId}/commit`, 'POST', {})

export const clearMigrationConflict = (seriesId: string) =>
  send<void>(`/api/migration/series/${seriesId}/clear-conflict`, 'POST', {})

export const commitAllCleanMigrationSeries = (batchId: string) =>
  send<void>(`/api/migration/batches/${batchId}/commit-clean`, 'POST', {})

// Stops the bulk commit if its job is still running (cooperative — takes effect between series, not
// mid-write), or resets the batch's stuck state directly if the job has already crashed.
export const cancelCommitAllCleanMigration = (batchId: string) =>
  send<void>(`/api/migration/batches/${batchId}/cancel-commit`, 'POST', {})

// Recovers a single series stuck at "Committing" because its commit job crashed. Rejected if the job
// still looks alive — only a confirmed-dead one can be reset.
export const resetStuckMigrationSeriesCommit = (seriesId: string) =>
  send<void>(`/api/migration/series/${seriesId}/reset-stuck-commit`, 'POST', {})

export const clearRankingConflicts = (batchId: string) =>
  send<{ clearedCount: number }>(`/api/migration/batches/${batchId}/clear-ranking-conflicts`, 'POST', {})

// Moves the series' whole inbox folder to the outbox and drops it from the batch — nothing is imported.
export const removeMigrationSeries = (seriesId: string) =>
  send<void>(`/api/migration/series/${seriesId}`, 'DELETE')

// --- MangaUpdates import wizard ------------------------------------------------------------------

export interface ImportItemDetail {
  id: string
  folderName: string
  fileName: string
  format: string
  parsedVolume: string | null
  pageCount: number
  sizeBytes: number
  include: boolean
  number: string | null
  volume: string | null
  title: string | null
  /** Already written into the library by an earlier commit attempt. A retry skips it, and the server
   *  rejects edits to it — its source file is gone, so there is nothing left to re-import. */
  imported: boolean
}

export interface ImportSeriesDetail {
  id: string
  groupTitle: string
  matchedSourceSeriesId: string | null
  matchedTitle: string | null
  titleOverride: string | null
  status: string
  existingLibrarySeriesId: string | null
  committedLibrarySeriesId: string | null
  commitItemsDone: number | null
  commitItemsTotal: number | null
  commitPageDone: number | null
  commitPageTotal: number | null
  commitError: string | null
  items: ImportItemDetail[]
  /** True when Status is "Committing" but the Hangfire job behind it is no longer actually alive —
   *  e.g. the app restarted mid-commit — so nothing is coming back to finish it. */
  commitJobCrashed: boolean
}

export interface ImportBatchDetail {
  id: string
  createdAt: string
  status: string
  error: string | null
  series: ImportSeriesDetail[]
  /** "Manga" | "Comic" — which library this batch commits into. */
  kind: string
  /** The metadata source this batch's series are matched against ("mangaupdates" | "comicvine").
   *  Search the *batch's* source when correcting a match, never a hardcoded one. */
  matchSourceId: string
}

export interface ImportBatchSummary {
  id: string
  createdAt: string
  status: string
  seriesCount: number
  error: string | null
}

export const startImportScan = () =>
  send<{ batchId: string }>(`/api/import/scan?kind=${currentKind()}`, 'POST', {})

export const getImportBatches = () => getJson<ImportBatchSummary[]>('/api/import/batches')

export const getImportBatch = (id: string) => getJson<ImportBatchDetail>(`/api/import/batches/${id}`)

/** One ranked match candidate. `year` and `chapterCount` are what actually tell two identically-titled
 *  comic volumes apart; `siteUrl` opens the candidate on the source's own site. */
export interface ImportCandidate {
  sourceSeriesId: string
  title: string
  altTitles: string[]
  coverUrl: string | null
  year: number | null
  chapterCount: number | null
  siteUrl: string | null
}

/** Ranked against the batch's source *and* the number of files this series has — not the generic
 *  /api/sources/{id}/search, which returns the source's own arbitrary ordering. */
export const searchImportCandidates = (seriesId: string, q: string) =>
  getJson<ImportCandidate[]>(`/api/import/series/${seriesId}/candidates?q=${encodeURIComponent(q)}`)

export const setImportSeriesMatch = (seriesId: string, sourceSeriesId: string | null) =>
  send<void>(`/api/import/series/${seriesId}/match`, 'PATCH', { sourceSeriesId })

export const setImportMergeTarget = (seriesId: string, existingLibrarySeriesId: string | null) =>
  send<void>(`/api/import/series/${seriesId}/merge-target`, 'PATCH', { existingLibrarySeriesId })

export const setImportTitleOverride = (seriesId: string, titleOverride: string | null) =>
  send<void>(`/api/import/series/${seriesId}/title`, 'PATCH', { titleOverride })

export const setImportItem = (
  itemId: string,
  include: boolean,
  number: string | null,
  volume: string | null,
  title: string | null,
) => send<void>(`/api/import/items/${itemId}`, 'PATCH', { include, number, volume, title })

export const commitImportSeries = (seriesId: string) =>
  send<void>(`/api/import/series/${seriesId}/commit`, 'POST', {})

// Recovers a series stuck at "Committing" because its commit job crashed. Rejected if the job still
// looks alive — only a confirmed-dead one can be reset.
export const resetStuckImportSeriesCommit = (seriesId: string) =>
  send<void>(`/api/import/series/${seriesId}/reset-stuck-commit`, 'POST', {})

// --- Notifications ---------------------------------------------------------------------------

export const getNotifications = () =>
  getJson<{ unread: number; items: AppNotification[] }>(`/api/notifications?kind=${currentKind()}`)

export const markNotificationsRead = (ids: string[]) =>
  send<void>('/api/notifications/read', 'POST', { ids })

// Scoped to the library you're looking at — clearing the comic bell must not silently mark every manga
// notification read too.
export const markAllNotificationsRead = () =>
  send<void>(`/api/notifications/read-all?kind=${currentKind()}`, 'POST', {})

// --- Collections -----------------------------------------------------------------------------
// Per-user, kind-scoped groupings of library series. Scoped by the active kind the same way the
// library is — a request that forgets ?kind= would return the other library's collections.

export interface Collection {
  id: string
  kind: string
  name: string
  description: string | null
  /** MemberSort enum name — see MEMBER_SORTS. */
  memberSort: string
  /** CollectionDashboardFilter enum name — see DASHBOARD_FILTERS. */
  dashboardFilter: string
  /** Cover endpoint URL (with a version stamp so it refetches after changes), or null if none yet. */
  coverUrl: string | null
  itemCount: number
  updatedAt: string
}

export interface CollectionMember {
  seriesId: string
  title: string
  coverUrl: string | null
}

export interface CollectionDetail extends Collection {
  coverIsCustom: boolean
  members: CollectionMember[]
}

/** Selectable member-sort options — value is the server enum name, label is user-facing. */
export const MEMBER_SORTS = [
  { v: 'Manual', l: 'Manual order' },
  { v: 'TitleAsc', l: 'Title (A–Z)' },
  { v: 'TitleDesc', l: 'Title (Z–A)' },
  { v: 'RecentlyAdded', l: 'Recently added' },
  { v: 'Year', l: 'Year (newest)' },
] as const

/** Which members surface on the dashboard rail — value is the server enum name. */
export const DASHBOARD_FILTERS = [
  { v: 'All', l: 'All members' },
  { v: 'Unread', l: 'Only unread & downloaded' },
] as const

export const getCollections = () => getJson<Collection[]>(`/api/collections?kind=${currentKind()}`)

/** Fetch a collection. Pass `forDashboard` to apply its dashboard filter to the returned members. */
export const getCollection = (id: string, forDashboard = false) =>
  getJson<CollectionDetail>(`/api/collections/${id}${forDashboard ? '?dashboard=true' : ''}`)

export const createCollection = (name: string, description?: string | null) =>
  send<Collection>(`/api/collections?kind=${currentKind()}`, 'POST', { name, description: description ?? null })

export const updateCollection = (
  id: string,
  name: string,
  description: string | null,
  memberSort: string,
  dashboardFilter: string,
) => send<void>(`/api/collections/${id}`, 'PUT', { name, description, memberSort, dashboardFilter })

export const deleteCollection = (id: string) => send<void>(`/api/collections/${id}`, 'DELETE')

export const addSeriesToCollection = (id: string, seriesId: string) =>
  send<void>(`/api/collections/${id}/series/${seriesId}`, 'POST')

export const removeSeriesFromCollection = (id: string, seriesId: string) =>
  send<void>(`/api/collections/${id}/series/${seriesId}`, 'DELETE')

export const reorderCollection = (id: string, seriesIds: string[]) =>
  send<void>(`/api/collections/${id}/order`, 'PUT', { seriesIds })

/** Which of the user's collections (ids) contain the given series — drives the add-to-collection ticks. */
export const getCollectionMembership = (seriesId: string) =>
  getJson<string[]>(`/api/collections/membership/${seriesId}`)

export const clearCollectionCover = (id: string) => send<void>(`/api/collections/${id}/cover`, 'DELETE')

/** Uploads a custom cover. Multipart, so it bypasses `send()`/jsonHeaders — the browser sets the
 * multipart boundary itself, which it can't do if we force a Content-Type. */
export async function uploadCollectionCover(id: string, file: File): Promise<void> {
  const form = new FormData()
  form.append('file', file)
  const res = await fetch(`/api/collections/${id}/cover`, { method: 'POST', credentials: 'include', body: form })
  if (!res.ok) throw new Error(await extractError(res))
}

export const setDashboardLayout = (items: DashboardItem[]) =>
  send<void>('/api/me/dashboard', 'PUT', { items })

// --- Error helper ----------------------------------------------------------------------------

async function extractError(res: Response): Promise<string> {
  try {
    const problem = await res.json()
    if (problem?.errors && typeof problem.errors === 'object') {
      const messages = Object.values(problem.errors as Record<string, string[]>).flat()
      if (messages.length) return messages.join(' ')
    }
    if (problem?.detail) return problem.detail as string
    if (problem?.title) return problem.title as string
    if (problem?.error) return problem.error as string
  } catch {
    /* fall through */
  }
  return `Request failed (${res.status}).`
}
