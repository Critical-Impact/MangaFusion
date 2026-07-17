<script lang="ts">
  import { session } from '../lib/session.svelte'
  import { THEMES, themeState, setTheme, type ThemeId } from '../lib/theme.svelte'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../lib/components/ui/select/index.js'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Input } from '../lib/components/ui/input/index.js'
  import { Label } from '../lib/components/ui/label/index.js'
  import { Spinner } from '../lib/components/ui/spinner/index.js'
  import { setUserDefaultLanguage, changeEmail, changePassword } from '../lib/api'
  import { notify } from '../lib/notify'
  import { languagesState, ensureLanguagesLoaded, languageName } from '../lib/languages.svelte'
  import { homeScope, setHomeAcrossLibraries, brandName } from '../lib/mode.svelte'
  import DashboardSettings from '../lib/DashboardSettings.svelte'

  ensureLanguagesLoaded()

  const HOME_SCOPES = [
    { v: 'scoped', l: 'Only the library I’m in' },
    { v: 'all', l: 'Both libraries' },
  ] as const

  const homeScopeValue = $derived(homeScope.acrossLibraries ? 'all' : 'scoped')

  let defaultLanguage = $state(session.me?.defaultLanguage ?? '')
  let savingLanguage = $state(false)

  async function saveDefaultLanguage() {
    savingLanguage = true
    try {
      await setUserDefaultLanguage(defaultLanguage || null)
      if (session.me) session.me.defaultLanguage = defaultLanguage || null
      notify.success('Default language saved.')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to save default language.')
    } finally {
      savingLanguage = false
    }
  }

  let email = $state(session.me?.email ?? '')
  let savingEmail = $state(false)

  async function saveEmail() {
    const next = email.trim()
    if (next === '') {
      notify.error('Email is required.')
      return
    }
    if (next.toLowerCase() === (session.me?.email ?? '').toLowerCase()) {
      notify.error('That is already your email.')
      return
    }
    savingEmail = true
    try {
      await changeEmail(next)
      if (session.me) session.me.email = next
      email = next
      notify.success('Email updated.')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to update email.')
    } finally {
      savingEmail = false
    }
  }

  let currentPassword = $state('')
  let newPassword = $state('')
  let confirmPassword = $state('')
  let savingPassword = $state(false)

  async function savePassword() {
    if (currentPassword === '') {
      notify.error('Enter your current password.')
      return
    }
    if (newPassword.length < 8) {
      notify.error('New password must be at least 8 characters.')
      return
    }
    if (newPassword !== confirmPassword) {
      notify.error('New passwords do not match.')
      return
    }
    savingPassword = true
    try {
      await changePassword(currentPassword, newPassword)
      currentPassword = ''
      newPassword = ''
      confirmPassword = ''
      notify.success('Password changed.')
    } catch (e) {
      notify.error(e instanceof Error ? e.message : 'Failed to change password.')
    } finally {
      savingPassword = false
    }
  }
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-5 text-[1.4rem]">Profile</h1>

  <div class="flex max-w-[520px] flex-col gap-[1.1rem]">
    <div class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Account</span>
      <form class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim" onsubmit={(e) => { e.preventDefault(); saveEmail() }}>
        <Label for="profile-email">Email</Label>
        <div class="flex items-center gap-2.5">
          <Input id="profile-email" type="email" class="w-auto min-w-64" bind:value={email} autocomplete="email" />
          <Button type="submit" size="sm" disabled={savingEmail}>
            {#if savingEmail}<Spinner />{/if}
            {savingEmail ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </form>
      <span class="muted text-[0.75rem]">You sign in with this address.</span>

      <form class="mt-2 flex flex-col gap-[0.5rem] text-[0.8rem] text-text-dim" onsubmit={(e) => { e.preventDefault(); savePassword() }}>
        <Label for="profile-current-password">Change password</Label>
        <Input
          id="profile-current-password"
          type="password"
          class="w-auto min-w-64"
          placeholder="Current password"
          bind:value={currentPassword}
          autocomplete="current-password"
        />
        <Input
          id="profile-new-password"
          type="password"
          class="w-auto min-w-64"
          placeholder="New password (min 8)"
          bind:value={newPassword}
          autocomplete="new-password"
        />
        <Input
          id="profile-confirm-password"
          type="password"
          class="w-auto min-w-64"
          placeholder="Confirm new password"
          bind:value={confirmPassword}
          autocomplete="new-password"
        />
        <div>
          <Button type="submit" size="sm" disabled={savingPassword}>
            {#if savingPassword}<Spinner />{/if}
            {savingPassword ? 'Saving…' : 'Change password'}
          </Button>
        </div>
      </form>
    </div>

    <div class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Appearance</span>
      <label class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
        Theme
        <Select type="single" value={themeState.id} onValueChange={(v) => setTheme(v as ThemeId)}>
          <SelectTrigger class="w-auto">
            {THEMES.find((t) => t.id === themeState.id)?.label}
          </SelectTrigger>
          <SelectContent>
            {#each THEMES as t (t.id)}<SelectItem value={t.id} label={t.label}>{t.label}</SelectItem>{/each}
          </SelectContent>
        </Select>
      </label>
      <span class="muted text-[0.75rem]">Saved to your account — follows you across devices.</span>
    </div>

    <div class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Reading</span>
      <label class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
        Default language
        <div class="flex items-center gap-2.5">
          <Select type="single" bind:value={defaultLanguage}>
            <SelectTrigger class="w-auto min-w-40">
              {defaultLanguage ? languageName(defaultLanguage) : 'None'}
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="" label="None">None</SelectItem>
              {#each languagesState.items as l (l.code)}
                <SelectItem value={l.code} label={l.name}>{l.name}</SelectItem>
              {/each}
            </SelectContent>
          </Select>
          <Button size="sm" onclick={saveDefaultLanguage} disabled={savingLanguage}>
            {#if savingLanguage}<Spinner />{/if}
            {savingLanguage ? 'Saving…' : 'Save'}
          </Button>
        </div>
      </label>
      <span class="muted text-[0.75rem]">
        Used to pre-fill the one-click "auto-download" action on a series page.
      </span>
    </div>

    <div class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Home page</span>
      <label class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
        Show on Home
        <Select
          type="single"
          value={homeScopeValue}
          onValueChange={(v) => setHomeAcrossLibraries(v === 'all')}
        >
          <SelectTrigger class="w-auto min-w-56">
            {HOME_SCOPES.find((s) => s.v === homeScopeValue)?.l}
          </SelectTrigger>
          <SelectContent>
            {#each HOME_SCOPES as s (s.v)}<SelectItem value={s.v} label={s.l}>{s.l}</SelectItem>{/each}
          </SelectContent>
        </Select>
      </label>
      <span class="muted text-[0.75rem]">
        Applies to Continue reading, Recently downloaded and Recently updated. By default these follow the
        library you're in, so {brandName()} only shows you {brandName() === 'ComicFusion' ? 'comics' : 'manga'}.
      </span>

      <div class="mt-3 flex flex-col gap-[0.4rem]">
        <span class="text-[0.8rem] text-text-dim">Dashboard layout</span>
        <DashboardSettings />
        <span class="muted text-[0.75rem]">
          Show or hide each rail and collection, and drag the order with the arrows. Collections are
          scoped to {brandName()}.
        </span>
      </div>
    </div>
  </div>
</section>
