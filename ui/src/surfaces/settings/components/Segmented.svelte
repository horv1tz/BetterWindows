<script lang="ts" generics="T extends string">
  import type { Component } from 'svelte';
  import { getRowContext } from '../rowContext';

  interface Option { value: T; label: string; icon?: Component<any>; title?: string; disabled?: boolean }
  let { options, value, onchange, ariaLabel, disabled = false, compact = false }: {
    options: readonly Option[]; value: T; onchange: (v: T) => void; ariaLabel?: string; disabled?: boolean; compact?: boolean;
  } = $props();
  const row = getRowContext();
  const index = $derived(Math.max(0, options.findIndex((o) => o.value === value)));
  const off = $derived(disabled || !!row?.().disabled);
  let host: HTMLElement | undefined = $state();

  function key(e: KeyboardEvent) {
    const dir = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!dir && e.key !== 'Home' && e.key !== 'End') return;
    e.preventDefault();
    const enabled = options.map((o, i) => (o.disabled ? -1 : i)).filter((i) => i >= 0);
    if (!enabled.length) return;
    let next: number;
    if (e.key === 'Home') next = enabled[0];
    else if (e.key === 'End') next = enabled[enabled.length - 1];
    else { const pos = enabled.indexOf(index); next = enabled[(pos + dir + enabled.length) % enabled.length]; }
    onchange(options[next].value);
    queueMicrotask(() => host?.querySelectorAll<HTMLElement>('[role=radio]')[next]?.focus());
  }
</script>

<div
  class="seg" class:compact bind:this={host} role="radiogroup" aria-label={ariaLabel} aria-labelledby={ariaLabel ? undefined : row?.().labelId}
  aria-disabled={off} style:--n={options.length} style:--i={index}
>
  <span class="thumb" aria-hidden="true"></span>
  {#each options as o, i (o.value)}
    {@const Icon = o.icon}
    <button
      type="button" role="radio" class="opt" class:sel={i === index} aria-checked={i === index} tabindex={i === index ? 0 : -1}
      title={o.title} disabled={off || o.disabled} onclick={() => onchange(o.value)} onkeydown={key}
    >
      {#if Icon}<Icon size={14} strokeWidth={2} aria-hidden="true" />{/if}
      <span>{o.label}</span>
    </button>
  {/each}
</div>

<style>
  .seg {
    position: relative; display: inline-grid; grid-auto-flow: column; grid-auto-columns: 1fr; padding: 2px; border-radius: 8px;
    background: var(--s-seg); isolation: isolate; max-width: 100%;
  }
  .thumb {
    position: absolute; z-index: 0; top: 2px; bottom: 2px; left: 2px; width: calc((100% - 4px) / var(--n));
    border-radius: 6px; background: var(--s-seg-thumb); box-shadow: var(--s-seg-thumb-shadow);
    transform: translateX(calc(var(--i) * 100%)); transition: transform var(--dur) var(--ease-out);
  }
  .opt {
    position: relative; z-index: 1; display: inline-flex; align-items: center; justify-content: center; gap: 6px; min-width: 0;
    height: 24px; padding: 0 14px; border: 0; border-radius: 6px; background: none; white-space: nowrap; font-weight: 500;
    color: var(--text-2);
  }
  .compact .opt { padding: 0 10px; }
  .opt.sel { color: var(--text); }
  .opt:not(.sel):not(:disabled):hover { color: var(--text); }
  .opt:disabled { opacity: 0.45; }
  .opt span { overflow: hidden; text-overflow: ellipsis; }
  .seg[aria-disabled='true'] { opacity: 0.6; }
</style>
