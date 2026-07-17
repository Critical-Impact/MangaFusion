<script lang="ts">
  import { onMount } from 'svelte'
  import { session } from '../../lib/session.svelte'
  import {
    getUsers,
    createUser,
    setUserRoles,
    disableUser,
    enableUser,
    deleteUser,
    type AdminUser,
  } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Input } from '../../lib/components/ui/input/index.js'
  import { Checkbox } from '../../lib/components/ui/checkbox/index.js'
  import { Label } from '../../lib/components/ui/label/index.js'
  import { Alert, AlertDescription } from '../../lib/components/ui/alert/index.js'
  import { Spinner } from '../../lib/components/ui/spinner/index.js'
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
  } from '../../lib/components/ui/alert-dialog/index.js'

  let users = $state<AdminUser[]>([])
  let loading = $state(true)
  let error = $state('')
  let busy = $state<Record<string, boolean>>({})

  // Create form
  let email = $state('')
  let password = $state('')
  let makeAdmin = $state(false)
  let creating = $state(false)

  onMount(load)

  async function load() {
    loading = true
    try {
      users = await getUsers()
    } catch (err) {
      error = msgOf(err)
    } finally {
      loading = false
    }
  }

  // Wrap a mutating action with per-row busy state + error capture + reload.
  async function act(id: string, fn: () => Promise<unknown>) {
    busy[id] = true
    try {
      await fn()
      users = await getUsers()
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      busy[id] = false
    }
  }

  function rolesWithAdmin(admin: boolean): string[] {
    return admin ? ['User', 'Admin'] : ['User']
  }

  async function toggleAdmin(u: AdminUser) {
    await act(u.id, () => setUserRoles(u.id, rolesWithAdmin(!u.roles.includes('Admin'))))
  }

  async function toggleDisabled(u: AdminUser) {
    await act(u.id, () => (u.disabled ? enableUser(u.id) : disableUser(u.id)))
  }

  async function remove(u: AdminUser) {
    await act(u.id, () => deleteUser(u.id))
  }

  async function create() {
    creating = true
    try {
      await createUser(email.trim(), password, rolesWithAdmin(makeAdmin))
      email = ''
      password = ''
      makeAdmin = false
      users = await getUsers()
      notify.success('User created.')
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      creating = false
    }
  }

  const isSelf = (u: AdminUser) => u.id === session.me?.id
  const msgOf = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')
</script>

{#if error}<Alert variant="destructive" class="mb-4"><AlertDescription>{error}</AlertDescription></Alert>{/if}

<form class="mb-[1.1rem] flex flex-wrap items-center gap-[0.6rem]" onsubmit={(e) => { e.preventDefault(); create() }}>
  <Input class="w-auto" type="email" placeholder="new user email" bind:value={email} required />
  <Input class="w-auto" type="password" placeholder="password (min 8)" bind:value={password} required minlength={8} />
  <div class="flex items-center gap-[0.35rem] text-[0.85rem] text-text-dim">
    <Checkbox id="make-admin" bind:checked={makeAdmin} />
    <Label for="make-admin">Admin</Label>
  </div>
  <Button type="submit" disabled={creating}>
    {#if creating}<Spinner />{/if}
    {creating ? 'Creating…' : 'Add user'}
  </Button>
</form>

{#if loading}
  <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
{:else}
  <ul class="m-0 list-none overflow-hidden rounded-[var(--r-md)] border border-border p-0">
    {#each users as u (u.id)}
      <li
        class={`grid grid-cols-[1fr_auto_auto] items-center gap-4 border-b border-border-dim px-4 py-[0.6rem] text-[0.9rem] last:border-b-0 ${u.disabled ? 'opacity-60' : ''}`}
      >
        <span class="flex min-w-0 items-center gap-2 overflow-hidden text-ellipsis">
          {u.email}
          {#if isSelf(u)}
            <span class="rounded-full border border-[#3a3357] px-[0.4rem] text-[0.68rem] text-brand-soft">you</span>
          {/if}
          {#if u.disabled}
            <span class="rounded-full border border-danger-border px-[0.4rem] text-[0.68rem] text-destructive">disabled</span>
          {/if}
        </span>
        <div class="flex items-center gap-[0.35rem] text-[0.85rem] text-text-dim" title="Administrator">
          <Checkbox
            id={`admin-${u.id}`}
            checked={u.roles.includes('Admin')}
            disabled={busy[u.id]}
            onCheckedChange={() => toggleAdmin(u)}
          />
          <Label for={`admin-${u.id}`}>Admin</Label>
        </div>
        <span class="flex justify-self-end gap-[0.4rem]">
          <Button variant="secondary" size="mini" disabled={busy[u.id] || isSelf(u)} onclick={() => toggleDisabled(u)}>
            {u.disabled ? 'Enable' : 'Disable'}
          </Button>
          <AlertDialog>
            <AlertDialogTrigger>
              {#snippet child({ props })}
                <Button
                  {...props}
                  variant="secondary"
                  size="mini"
                  class="border-danger-border text-destructive hover:border-destructive"
                  disabled={busy[u.id] || isSelf(u)}
                >
                  Delete
                </Button>
              {/snippet}
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete {u.email}?</AlertDialogTitle>
                <AlertDialogDescription>
                  This removes their reading progress, follows, and notifications. This cannot be undone.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onclick={() => remove(u)}>Delete</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </span>
      </li>
    {/each}
  </ul>
{/if}
