import { getMe, login, logout, register, type Me } from './api'

// Reactive session shared across the app (Svelte 5 runes module state).
export const session = $state<{ me: Me | null; loading: boolean }>({
  me: null,
  loading: true,
})

export async function loadSession(): Promise<void> {
  session.loading = true
  try {
    session.me = await getMe()
  } finally {
    session.loading = false
  }
}

export async function doLogin(email: string, password: string): Promise<void> {
  await login(email, password)
  session.me = await getMe()
}

export async function doRegister(email: string, password: string): Promise<void> {
  await register(email, password)
}

export async function doLogout(): Promise<void> {
  await logout()
  session.me = null
}

export function isAdmin(): boolean {
  return session.me?.roles.includes('Admin') ?? false
}
