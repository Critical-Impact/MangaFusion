// Shared entry point for transient, request-triggered feedback (action succeeded/failed) — the
// counterpart to the notification bell, which is for durable, async/background events. Wraps
// svelte-sonner so call sites don't import the toast library directly.
import { toast } from 'svelte-sonner'

export const notify = {
  success: (message: string) => toast.success(message),
  error: (message: string) => toast.error(message),
  warning: (message: string) => toast.warning(message),
  info: (message: string) => toast.info(message),
}
