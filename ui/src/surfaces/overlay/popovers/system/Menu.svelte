<script lang="ts" module>
  export type MenuItemEntry = {
    kind: 'item';
    id: string;
    label: string;
    /** Right-aligned secondary text such as a shortcut hint. Display only. */
    hint?: string;
    /** Present (true/false) = a toggle item: role menuitemcheckbox, drawn with a small switch at the trailing edge. */
    checked?: boolean;
    disabled?: boolean;
    onselect: () => void;
  };
  export type MenuSeparator = { kind: 'separator'; id: string };
  export type MenuEntry = MenuItemEntry | MenuSeparator;
</script>

<script lang="ts">
  /**
   * A macOS-style menu list for popovers: accent highlight under the pointer or the keyboard focus, one highlighted row at a time
   * (hovering moves DOM focus, so mouse and keyboard never disagree), arrow keys / Home / End / type-ahead, Enter or Space selects.
   * Escape is deliberately not handled here: it bubbles to the overlay, which closes the popover.
   */
  import type { Snippet } from 'svelte';
  import { nextIndex, typeahead } from './menuNav';

  let { entries, label, header, minWidth = 0 }: { entries: MenuEntry[]; label: string; header?: Snippet; minWidth?: number } = $props();

  let menuEl = $state<HTMLElement>();
  const enabledRows = () => [...(menuEl?.querySelectorAll<HTMLButtonElement>('[role^="menuitem"]:not([aria-disabled="true"])') ?? [])];

  // The menu itself takes focus on open so Escape / arrow keys work immediately; no row is highlighted until the user moves.
  $effect(() => { menuEl?.focus({ preventScroll: true }); });

  let buffer = '';
  let bufferTimer: ReturnType<typeof setTimeout> | undefined;

  function onkeydown(e: KeyboardEvent) {
    if (e.ctrlKey || e.altKey || e.metaKey) return;
    const rows = enabledRows();
    const at = rows.indexOf(document.activeElement as HTMLButtonElement);
    let key: 'ArrowDown' | 'ArrowUp' | 'Home' | 'End' | null = null;
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Home' || e.key === 'End') key = e.key;
    else if (e.key === 'Tab') key = e.shiftKey ? 'ArrowUp' : 'ArrowDown';
    if (key) {
      e.preventDefault();
      rows[nextIndex(rows.length, at, key)]?.focus({ preventScroll: true });
      return;
    }
    if (e.key.length === 1 && /\S/.test(e.key)) {
      buffer += e.key;
      clearTimeout(bufferTimer);
      bufferTimer = setTimeout(() => (buffer = ''), 700);
      // Repeating one letter cycles through the items starting with it (like native menus).
      const q = buffer.length > 1 && [...buffer].every((c) => c === buffer[0]) ? buffer[0] : buffer;
      const hit = typeahead(rows.map((r) => r.textContent ?? ''), at, q);
      if (hit >= 0) rows[hit].focus({ preventScroll: true });
    }
  }
</script>

<div
  bind:this={menuEl}
  class="menu"
  role="menu"
  aria-label={label}
  tabindex="-1"
  style:min-width="{minWidth}px"
  {onkeydown}
>
  {#if header}
    {@render header()}
    <div class="sep" role="separator"></div>
  {/if}
  {#each entries as entry (entry.id)}
    {#if entry.kind === 'separator'}
      <div class="sep" role="separator"></div>
    {:else}
      <button
        type="button"
        class="row"
        role={entry.checked === undefined ? 'menuitem' : 'menuitemcheckbox'}
        aria-checked={entry.checked === undefined ? undefined : entry.checked}
        aria-disabled={entry.disabled ? 'true' : undefined}
        tabindex="-1"
        onpointerenter={(e) => { if (!entry.disabled) e.currentTarget.focus({ preventScroll: true }); }}
        onpointerleave={() => menuEl?.focus({ preventScroll: true })}
        onclick={() => { if (!entry.disabled) entry.onselect(); }}
      >
        <span class="label">{entry.label}</span>
        {#if entry.hint}<span class="hint">{entry.hint}</span>{/if}
        {#if entry.checked !== undefined}<span class="switch" class:on={entry.checked} aria-hidden="true"></span>{/if}
      </button>
    {/if}
  {/each}
</div>

<style>
  .menu {
    display: flex;
    flex-direction: column;
    outline: none;
    min-width: 0;
  }

  .row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto auto;
    align-items: center;
    width: 100%;
    height: 24px;
    margin: 0;
    padding: 0 10px 0 12px;
    border: 0;
    border-radius: 5px;
    background: transparent;
    color: var(--text);
    font: inherit;
    font-size: 13px;
    line-height: 1;
    text-align: left;
    white-space: nowrap;
    cursor: default;
    outline: none;
  }
  .label { overflow: hidden; text-overflow: ellipsis; line-height: 1.3; }
  .hint { margin-left: 18px; font-size: 12px; color: var(--text-2); font-variant-numeric: tabular-nums; }

  .row[aria-disabled='true'] { color: var(--text-3); }
  .row:focus:not([aria-disabled='true']) { background: var(--accent); color: #fff; }
  .row:focus:not([aria-disabled='true']) .hint { color: rgba(255, 255, 255, 0.78); }

  /* Toggle switch (trailing). Only transform / opacity animate. */
  .switch {
    position: relative;
    width: 26px;
    height: 15px;
    margin-left: 14px;
    border-radius: 8px;
    background: color-mix(in oklab, var(--text) 24%, transparent);
    transition: background-color var(--dur-fast) var(--ease-out);
  }
  .switch::after {
    content: '';
    position: absolute;
    left: 1.5px;
    top: 1.5px;
    width: 12px;
    height: 12px;
    border-radius: 50%;
    background: #fff;
    box-shadow: 0 1px 2px rgba(0, 0, 0, 0.35);
    transition: transform var(--dur-fast) var(--ease-out);
  }
  .switch.on { background: #30d158; }
  .switch.on::after { transform: translateX(11px); }
  .row:focus:not([aria-disabled='true']) .switch { background: rgba(255, 255, 255, 0.35); }
  .row:focus:not([aria-disabled='true']) .switch.on { background: #30d158; }

  .sep { height: 1px; margin: 5px 10px; background: var(--separator); flex: none; }
</style>
