<script lang="ts">
  import type { Snippet } from 'svelte'
  import { link } from 'svelte-spa-router'
  import Cover from './Cover.svelte'

  let {
    title,
    subtitle,
    coverUrl,
    href,
    onclick,
    radius = 'var(--r-md)',
    overlay,
  }: {
    title: string
    subtitle?: string
    coverUrl?: string | null
    href?: string
    onclick?: () => void
    radius?: string
    overlay?: Snippet
  } = $props()

  const cardClass = 'group flex w-full cursor-pointer flex-col gap-[0.4rem] border-0 bg-transparent p-0 text-left text-inherit no-underline'
</script>

{#if href}
  <a class={cardClass} {href} use:link {title}>
    <Cover src={coverUrl} alt={title} {radius} {overlay} />
    <span class="block max-w-full truncate px-[var(--poster-pad)] text-[0.9rem] font-semibold">{title}</span>
    {#if subtitle}
      <span class="block max-w-full truncate px-[var(--poster-pad)] text-[0.78rem] text-text-mute">{subtitle}</span>
    {/if}
  </a>
{:else}
  <!-- A real <button> would nest invalidly with any interactive controls (e.g. an "add" button)
       inside the overlay slot, so this is a div with button semantics instead. -->
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div
    class={cardClass}
    role="button"
    tabindex="0"
    {title}
    onclick={() => onclick?.()}
    onkeydown={(e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        onclick?.()
      }
    }}
  >
    <Cover src={coverUrl} alt={title} {radius} {overlay} />
    <span class="block max-w-full truncate px-[var(--poster-pad)] text-[0.9rem] font-semibold">{title}</span>
    {#if subtitle}
      <span class="block max-w-full truncate px-[var(--poster-pad)] text-[0.78rem] text-text-mute">{subtitle}</span>
    {/if}
  </div>
{/if}
