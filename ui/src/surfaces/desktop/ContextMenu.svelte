<script lang="ts">
  // Compact in-surface context menu (icons, widgets). Desktop-menu popovers for the empty desktop live in the overlay instead.
  import { onMount } from 'svelte';
  import { Check } from '@lucide/svelte';
  import { clamp } from './layout';
  import type { MenuItem } from './menu';

  let { x, y, items, label = '', onselect, onclose }: {
    x: number; y: number; items: MenuItem[]; label?: string; onselect: (id: string) => void; onclose: () => void;
  } = $props();

  let el: HTMLDivElement;
  let w = $state(200);
  let h = $state(120);
  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  const pos = $derived({ left: clamp(x, 6, vw - w - 6), top: clamp(y, 6, vh - h - 6) });
  const actionable = $derived(items.filter((i) => !i.separator && !i.disabled));
  let active = $state(0);

  function activate(i: MenuItem) { if (!i.disabled && !i.separator) onselect(i.id); }

  function focusAt(n: number) {
    active = (n + actionable.length) % Math.max(1, actionable.length);
    el.querySelector<HTMLElement>(`[data-id="${CSS.escape(actionable[active]?.id ?? '')}"]`)?.focus();
  }

  function onkey(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') { e.preventDefault(); focusAt(active + 1); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); focusAt(active - 1); }
    else if (e.key === 'Home') { e.preventDefault(); focusAt(0); }
    else if (e.key === 'End') { e.preventDefault(); focusAt(actionable.length - 1); }
    else if (e.key === 'Escape' || e.key === 'Tab') { e.preventDefault(); e.stopPropagation(); onclose(); }
  }

  onMount(() => {
    focusAt(0);
    const down = (e: PointerEvent) => { if (!el.contains(e.target as Node)) onclose(); };
    // capture: a click on a widget or icon must close the menu even if that element stops propagation
    window.addEventListener('pointerdown', down, true);
    window.addEventListener('blur', onclose);
    return () => { window.removeEventListener('pointerdown', down, true); window.removeEventListener('blur', onclose); };
  });
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} />

<!-- svelte-ignore a11y_no_noninteractive_element_interactions -->
<div
  class="menu glass" role="menu" aria-label={label} tabindex="-1" bind:this={el}
  bind:offsetWidth={w} bind:offsetHeight={h}
  style:left="{pos.left}px" style:top="{pos.top}px"
  onkeydown={onkey}
  oncontextmenu={(e) => e.preventDefault()}
>
  {#each items as item (item.id)}
    {#if item.separator}
      <div class="sep" role="separator"></div>
    {:else}
      <button
        type="button" role={item.checked !== undefined ? 'menuitemradio' : 'menuitem'} aria-checked={item.checked}
        class="item" class:danger={item.danger} data-id={item.id} disabled={item.disabled}
        onclick={() => activate(item)}
        onpointerenter={(e) => (e.currentTarget as HTMLElement).focus()}
      >
        <span class="check">{#if item.checked}<Check size={13} strokeWidth={2.6} />{/if}</span>
        <span class="text">{item.label}</span>
      </button>
    {/if}
  {/each}
</div>

<style>
  .menu {
    position: fixed; z-index: 1000; min-width: 188px; padding: 5px; border-radius: 10px;
    box-shadow: var(--shadow-lg); background: var(--glass-bg-strong);
    animation: pop var(--dur-fast) var(--ease-out); transform-origin: top left; outline: none;
  }
  .item {
    display: flex; align-items: center; gap: 4px; width: 100%; height: 26px; padding: 0 10px 0 4px;
    border: 0; border-radius: 6px; background: none; text-align: left; cursor: default; color: var(--text); font-size: 13px;
  }
  .item:focus-visible, .item:hover:not(:disabled) { outline: none; background: var(--accent); color: #fff; }
  .item.danger { color: #ff453a; }
  .item.danger:focus-visible, .item.danger:hover:not(:disabled) { color: #fff; background: #ff453a; }
  .item:disabled { opacity: 0.4; }
  .check { width: 18px; display: inline-grid; place-items: center; flex: none; }
  .text { flex: 1; white-space: nowrap; }
  .sep { height: 1px; margin: 5px 8px; background: var(--separator); }
  @keyframes pop { from { opacity: 0; transform: scale(0.96); } to { opacity: 1; transform: none; } }
</style>
