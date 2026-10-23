<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { Rect } from '$lib/protocol/generated';
  import { toRect } from './popoverTracker';

  /**
   * One 22 DIP-high hit target in the bar. The visible highlight is a pseudo element whose opacity animates (compositor only);
   * a second pseudo element stretches the hit area to the full 28 DIP bar height so the top and bottom pixels are clickable too.
   */
  let {
    label,
    pressed = false,
    dim = false,
    disabled = false,
    haspopup = 'dialog',
    strong = false,
    onactivate,
    onsecondary,
    children,
  }: {
    label: string;
    pressed?: boolean;
    dim?: boolean;
    disabled?: boolean;
    /** What the item opens; null for items that open nothing in the overlay (tray icons): no aria-haspopup / aria-expanded then. */
    haspopup?: 'dialog' | 'menu' | null;
    /** Semibold text (the app name). */
    strong?: boolean;
    /** Primary activation (click / Enter / Space) with the item's bounding rect in client DIPs. */
    onactivate?: (anchor: Rect) => void;
    /** Context menu / double click variants (tray icons). */
    onsecondary?: (kind: 'right' | 'double', anchor: Rect) => void;
    children: Snippet;
  } = $props();

  let el = $state<HTMLButtonElement>();
  const rect = () => toRect(el!.getBoundingClientRect());
</script>

<button
  bind:this={el}
  type="button"
  class="item"
  class:dim
  class:strong
  data-bar-item
  data-pressed={pressed || undefined}
  aria-label={label}
  aria-haspopup={haspopup ?? undefined}
  aria-expanded={haspopup ? pressed : undefined}
  {disabled}
  onclick={() => onactivate?.(rect())}
  oncontextmenu={(e) => { if (onsecondary) { e.preventDefault(); onsecondary('right', rect()); } }}
  ondblclick={() => onsecondary?.('double', rect())}
>
  {@render children()}
</button>

<style>
  .item {
    position: relative;
    isolation: isolate;
    display: inline-flex;
    align-items: center;
    gap: 5px;
    min-width: 0; /* lets the app name shrink (ellipsis) when the bar is narrow */
    height: 22px;
    padding: 0 7px;
    margin: 0;
    border: 0;
    border-radius: 5px;
    background: transparent;
    color: var(--text);
    font: inherit;
    letter-spacing: -0.003em;
    white-space: nowrap;
    cursor: default;
    -webkit-tap-highlight-color: transparent;
    outline: none;
  }
  .item.strong { font-weight: 650; }
  .item.dim { color: var(--text-2); }
  .item:disabled { color: var(--text-2); }

  /* Highlight layer. Opacity is the only thing that animates. */
  .item::before {
    content: '';
    position: absolute;
    inset: 0;
    z-index: -1;
    border-radius: inherit;
    background: var(--hover);
    opacity: 0;
    transition: opacity var(--dur-fast) var(--ease-out);
  }
  /* Full-height hit area: the bar is 28 DIP, the visible target 22. */
  .item::after {
    content: '';
    position: absolute;
    inset: -3px -1px;
  }
  .item:not(:disabled):hover::before { opacity: 1; }
  .item:not(:disabled):active::before,
  .item[data-pressed]::before {
    background: var(--press);
    opacity: 1;
    transition: none;
  }
  .item:focus-visible { box-shadow: 0 0 0 2px var(--accent); }
</style>
