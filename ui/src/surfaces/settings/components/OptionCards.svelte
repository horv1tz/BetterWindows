<script lang="ts" generics="T extends string">
  import type { Snippet } from 'svelte';
  import { getRowContext } from '../rowContext';

  interface Option { value: T; label: string; title?: string }
  let { options, value, onchange, art, ariaLabel, columns = 3, disabled = false }: {
    options: readonly Option[]; value: T; onchange: (v: T) => void; art: Snippet<[T]>; ariaLabel?: string; columns?: number; disabled?: boolean;
  } = $props();
  const row = getRowContext();
  let host: HTMLElement | undefined = $state();
  const index = $derived(Math.max(0, options.findIndex((o) => o.value === value)));

  function key(e: KeyboardEvent) {
    const d = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const next = (index + d + options.length) % options.length;
    onchange(options[next].value);
    queueMicrotask(() => host?.querySelectorAll<HTMLElement>('[role=radio]')[next]?.focus());
  }
</script>

<div class="cards" role="radiogroup" aria-label={ariaLabel} aria-labelledby={ariaLabel ? undefined : row?.().labelId} bind:this={host} style:--cols={columns}>
  {#each options as o, i (o.value)}
    <button type="button" role="radio" class="card" class:sel={i === index} aria-checked={i === index} tabindex={i === index ? 0 : -1} title={o.title} {disabled} onclick={() => onchange(o.value)} onkeydown={key}>
      <span class="art">{@render art(o.value)}</span>
      <span class="lbl">{o.label}</span>
    </button>
  {/each}
</div>

<style>
  .cards { display: grid; grid-template-columns: repeat(var(--cols), minmax(0, 1fr)); gap: 12px; padding: 14px; }
  .card { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 0; border: 0; background: none; min-width: 0; color: var(--text-2); }
  .art {
    display: grid; place-items: center; width: 100%; padding: 6px; border-radius: 12px; border: 2px solid transparent;
    transition: transform var(--dur) var(--ease-out);
  }
  .card:hover:not(:disabled) .art { transform: translateY(-1px); }
  .card.sel .art { border-color: var(--accent); }
  .card.sel { color: var(--text); }
  .lbl { font-weight: 500; }
  .card.sel .lbl { font-weight: 600; }
  .card:disabled { opacity: 0.5; }
  .card:focus-visible { outline: none; }
  .card:focus-visible .art { outline: 2px solid var(--focus); outline-offset: 1px; }
</style>
