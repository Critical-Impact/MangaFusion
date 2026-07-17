<script lang="ts">
  import { onMount } from 'svelte'
  import { getAdminSettings, putAdminSettings, type AdminSettings } from '../../lib/api'
  import { notify } from '../../lib/notify'
  import { Button } from '../../lib/components/ui/button/index.js'
  import { Input } from '../../lib/components/ui/input/index.js'
  import { Checkbox } from '../../lib/components/ui/checkbox/index.js'
  import { Label } from '../../lib/components/ui/label/index.js'
  import { Select, SelectTrigger, SelectContent, SelectItem } from '../../lib/components/ui/select/index.js'
  import { Spinner } from '../../lib/components/ui/spinner/index.js'

  let cron = $state('')
  let langs = $state('')
  let grace = $state(0)
  let allowReg = $state(true)
  let logLevel = $state('')
  let loading = $state(true)
  let saving = $state(false)

  const cronPresets = [
    { label: 'Hourly', value: '0 * * * *' },
    { label: 'Every 6h', value: '0 */6 * * *' },
    { label: 'Daily 3am', value: '0 3 * * *' },
  ]

  const logLevels = ['', 'Trace', 'Debug', 'Information', 'Warning', 'Error', 'Critical', 'None']
  const logLevelLabel = (l: string) => l || 'Default (quiet)'

  onMount(load)

  async function load() {
    loading = true
    try {
      apply(await getAdminSettings())
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      loading = false
    }
  }

  function apply(s: AdminSettings) {
    cron = s.monitorCron
    langs = s.defaultLanguages.join(', ')
    grace = s.defaultGraceDays
    allowReg = s.allowSelfRegistration
    logLevel = s.minimumLogLevel ?? ''
  }

  async function save() {
    saving = true
    try {
      apply(
        await putAdminSettings({
          monitorCron: cron.trim(),
          defaultLanguages: langs.split(',').map((x) => x.trim()).filter(Boolean),
          defaultGraceDays: grace,
          allowSelfRegistration: allowReg,
          minimumLogLevel: logLevel,
        }),
      )
      notify.success('Settings saved.')
    } catch (err) {
      notify.error(msgOf(err))
    } finally {
      saving = false
    }
  }

  const msgOf = (e: unknown) => (e instanceof Error ? e.message : 'Something went wrong.')
</script>

{#if loading}
  <p class="muted flex items-center gap-2"><Spinner />Loading…</p>
{:else}
  <div class="flex max-w-[520px] flex-col gap-[1.1rem]">
    <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Monitor schedule (cron)</span>
      <Input bind:value={cron} placeholder="0 * * * *" />
      <span class="mt-[0.1rem] flex gap-[0.4rem]">
        {#each cronPresets as p (p.value)}
          <button
            type="button"
            class="cursor-pointer [font:inherit] rounded-[var(--r-pill)] border border-input bg-surface-3 px-[0.5rem] py-[0.15rem] text-[0.72rem] text-text-dim hover:border-brand-soft hover:text-foreground"
            onclick={() => (cron = p.value)}
          >
            {p.label}
          </button>
        {/each}
      </span>
      <span class="muted text-[0.75rem]">Standard 5-field cron. When followed series are checked for new chapters.</span>
    </label>

    <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Default auto-download languages</span>
      <Input bind:value={langs} placeholder="en, es" />
      <span class="muted text-[0.75rem]">Used when a followed/auto series has no languages of its own.</span>
    </label>

    <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Default grace period (days)</span>
      <Input class="max-w-[8rem]" type="number" min="0" bind:value={grace} />
      <span class="muted text-[0.75rem]">Wait this long for a preferred group before taking a non-preferred release.</span>
    </label>

    <div class="flex flex-wrap items-center gap-[0.4rem] text-[0.85rem] text-text-2">
      <Checkbox id="allow-self-registration" bind:checked={allowReg} />
      <Label for="allow-self-registration">Allow self-registration</Label>
      <span class="muted text-[0.75rem]">When off, only an admin can create new accounts.</span>
    </div>

    <label class="flex flex-col gap-[0.35rem] text-[0.85rem] text-text-2">
      <span>Minimum log level</span>
      <Select type="single" bind:value={logLevel}>
        <SelectTrigger class="max-w-[8rem]">{logLevelLabel(logLevel)}</SelectTrigger>
        <SelectContent>
          {#each logLevels as l}<SelectItem value={l} label={logLevelLabel(l)}>{logLevelLabel(l)}</SelectItem>{/each}
        </SelectContent>
      </Select>
      <span class="muted text-[0.75rem]">
        Default keeps EF Core SQL, HTTP client, and Hangfire logging quiet (Warning+). Lowering this
        (e.g. to Debug or Trace) reveals those too, along with more detail from the app itself.
        Applies immediately, no restart needed.
      </span>
    </label>

    <div class="mt-[0.3rem] flex items-center gap-[0.8rem]">
      <Button onclick={save} disabled={saving}>
        {#if saving}<Spinner />{/if}
        {saving ? 'Saving…' : 'Save settings'}
      </Button>
    </div>
  </div>
{/if}
