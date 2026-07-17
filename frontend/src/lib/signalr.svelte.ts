import * as signalr from '@microsoft/signalr'

export interface DownloadProgress {
  downloadId: string
  chapterId: string | null
  status: string
  pagesDone: number
  pagesTotal: number
}

export interface ImportCommitProgress {
  importSeriesId: string
  status: string
  itemsDone: number
  itemsTotal: number
  pageDone: number | null
  pageTotal: number | null
}

// Reactive map of live download progress keyed by downloadId (and mirrored by chapterId for the
// series view). Populated from the SignalR hub.
export const progressByDownload = $state<Record<string, DownloadProgress>>({})
export const progressByChapter = $state<Record<string, DownloadProgress>>({})

// Reactive map of live import-wizard commit progress, keyed by import series id.
export const progressByImportSeries = $state<Record<string, ImportCommitProgress>>({})

// Bumped whenever a new notification arrives, so the bell can refetch.
export const realtime = $state<{ notificationTick: number }>({ notificationTick: 0 })

let connection: signalr.HubConnection | null = null

export async function startSignalR(): Promise<void> {
  if (connection) return

  connection = new signalr.HubConnectionBuilder()
    .withUrl('/hubs/library')
    .withAutomaticReconnect()
    .build()

  connection.on('downloadProgress', (msg: DownloadProgress) => {
    progressByDownload[msg.downloadId] = msg
    if (msg.chapterId) {
      progressByChapter[msg.chapterId] = msg
    }
  })

  connection.on('importCommitProgress', (msg: ImportCommitProgress) => {
    progressByImportSeries[msg.importSeriesId] = msg
  })

  connection.on('notification', () => {
    realtime.notificationTick++
  })

  try {
    await connection.start()
  } catch (err) {
    console.error('SignalR connection failed', err)
  }
}
