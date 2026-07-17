<script lang="ts">
  import { brandName } from './mode.svelte'
  import { doLogin, doRegister } from './session.svelte'
  import { Button } from './components/ui/button/index.js'
  import { Input } from './components/ui/input/index.js'
  import { Alert, AlertDescription } from './components/ui/alert/index.js'
  import { Spinner } from './components/ui/spinner/index.js'

  let mode = $state<'login' | 'register'>('login')
  let email = $state('')
  let password = $state('')
  let error = $state('')
  let submitting = $state(false)

  async function submit(e: SubmitEvent) {
    e.preventDefault()
    error = ''
    submitting = true
    try {
      if (mode === 'register') {
        await doRegister(email, password)
      }
      await doLogin(email, password)
    } catch (err) {
      error = err instanceof Error ? err.message : 'Something went wrong.'
    } finally {
      submitting = false
    }
  }
</script>

<main class="center">
  <section class="card w-full max-w-[22rem] p-8">
    <h1 class="mb-1 text-[1.6rem] text-brand-soft">{brandName()}</h1>
    <p class="muted">{mode === 'login' ? 'Sign in to continue' : 'Create an account'}</p>
    <form class="mt-5 flex flex-col gap-[0.9rem]" onsubmit={submit}>
      <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-dim">
        Email
        <Input type="email" bind:value={email} autocomplete="username" required />
      </label>
      <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-dim">
        Password
        <Input
          type="password"
          bind:value={password}
          autocomplete={mode === 'login' ? 'current-password' : 'new-password'}
          required
        />
      </label>
      {#if error}<Alert variant="destructive"><AlertDescription>{error}</AlertDescription></Alert>{/if}
      <Button type="submit" disabled={submitting}>
        {#if submitting}<Spinner />{/if}
        {submitting ? 'Please wait…' : mode === 'login' ? 'Sign in' : 'Register'}
      </Button>
    </form>
    <p class="muted mt-4 text-[0.8rem]">
      {#if mode === 'login'}
        No account?
        <button
          type="button"
          class="cursor-pointer border-0 bg-transparent p-0 font-semibold text-brand-soft underline"
          onclick={() => { mode = 'register'; error = '' }}
        >Register</button>
      {:else}
        Already registered?
        <button
          type="button"
          class="cursor-pointer border-0 bg-transparent p-0 font-semibold text-brand-soft underline"
          onclick={() => { mode = 'login'; error = '' }}
        >Sign in</button>
      {/if}
    </p>
  </section>
</main>
