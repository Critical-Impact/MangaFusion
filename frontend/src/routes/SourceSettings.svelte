<script lang="ts">
  import { onMount } from 'svelte'
  import {
    getSources,
    getCredentialFields,
    setCredentials,
    testCredentials,
    type SourceSummary,
    type CredentialField,
  } from '../lib/api'
  import { Button } from '../lib/components/ui/button/index.js'
  import { Input } from '../lib/components/ui/input/index.js'
  import { Alert, AlertDescription } from '../lib/components/ui/alert/index.js'

  let sources = $state<SourceSummary[]>([])
  let fields = $state<Record<string, CredentialField[]>>({})
  let values = $state<Record<string, Record<string, string>>>({})
  let status = $state<Record<string, string>>({})
  let busy = $state<Record<string, boolean>>({})
  let error = $state('')

  onMount(async () => {
    try {
      sources = await getSources()
      for (const s of sources) {
        if (s.requiresAuth) {
          const f = await getCredentialFields(s.id)
          fields[s.id] = f
          values[s.id] = Object.fromEntries(f.map((x) => [x.name, '']))
        }
      }
    } catch (err) {
      error = err instanceof Error ? err.message : 'Failed to load sources.'
    }
  })

  async function save(id: string) {
    busy[id] = true
    status[id] = ''
    try {
      await setCredentials(id, values[id])
      const idx = sources.findIndex((s) => s.id === id)
      if (idx >= 0) sources[idx] = { ...sources[idx], configured: true }
      status[id] = 'Saved. Use “Test” to verify.'
    } catch (err) {
      status[id] = err instanceof Error ? err.message : 'Save failed.'
    } finally {
      busy[id] = false
    }
  }

  async function test(id: string) {
    busy[id] = true
    status[id] = 'Testing…'
    try {
      const ok = await testCredentials(id)
      status[id] = ok ? 'Credentials valid ✓' : 'Authentication failed ✗'
    } catch (err) {
      status[id] = err instanceof Error ? err.message : 'Test failed.'
    } finally {
      busy[id] = false
    }
  }
</script>

<section class="mx-auto max-w-[1100px] px-5 py-6">
  <h1 class="mb-4 text-[1.4rem]">Sources</h1>
  {#if error}<Alert variant="destructive" class="mb-4"><AlertDescription>{error}</AlertDescription></Alert>{/if}

  {#each sources as source (source.id)}
    <div class="card mb-4 p-5">
      <div class="flex items-center justify-between gap-4">
        <div>
          <span class="mr-[0.6rem] font-semibold">{source.displayName}</span>
          <span class="muted text-[0.75rem]">{source.capabilities.join(' · ')}</span>
        </div>
        {#if source.requiresAuth}
          <span
            class={`rounded-full px-[0.55rem] py-[0.2rem] text-[0.72rem] whitespace-nowrap ${source.configured ? 'bg-[#22392a] text-ok' : 'bg-[#3a2a2a] text-[#f0a0a0]'}`}
          >
            {source.configured ? 'Configured' : 'Not configured'}
          </span>
        {/if}
      </div>

      {#if source.requiresAuth && fields[source.id]}
        <div class="my-[1.1rem] grid grid-cols-[repeat(auto-fit,minmax(220px,1fr))] gap-[0.8rem]">
          {#each fields[source.id] as field (field.name)}
            <label class="flex flex-col gap-[0.3rem] text-[0.8rem] text-text-dim">
              {field.label}
              <Input
                type={field.secret ? 'password' : 'text'}
                autocomplete="off"
                bind:value={values[source.id][field.name]}
                placeholder={source.configured ? '•••••• (stored)' : ''}
              />
            </label>
          {/each}
        </div>
        <div class="flex items-center gap-3">
          <Button onclick={() => save(source.id)} disabled={busy[source.id]}>Save</Button>
          <Button variant="secondary" onclick={() => test(source.id)} disabled={busy[source.id]}>Test</Button>
          {#if status[source.id]}<span class="muted text-[0.85rem]">{status[source.id]}</span>{/if}
        </div>
        <p class="muted mt-[0.9rem] text-[0.78rem]">
          Create a Personal API Client in your MangaDex account settings, then enter its Client ID
          and Secret together with your MangaDex username and password.
        </p>
      {/if}
    </div>
  {/each}
</section>
