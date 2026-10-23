<script lang="ts">
  import type { Snippet } from 'svelte';
  import type { Rect } from '$lib/protocol/generated';
  import { glassShape } from '$lib/shape';
  import { placePanel, type DockSide } from './placement';

  /**
   * Glass panel for the dock popovers: like PopoverFrame, but it knows which screen edge the dock is on, so on left/right docks
   * the panel opens beside the icon instead of above it, and it grows out of the icon it belongs to.
   */
  let { anchor, dock, width = 264, label = '', role = 'dialog', children }: {
    anchor: Rect; dock: DockSide; width?: number; label?: string; role?: 'dialog' | 'menu'; children: Snippet;
  } = $props();

  let w = $state(0);
  let h = $state(0);
  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  const measured = $derived(w > 0 && h > 0);
  const placed = $derived(placePanel({ anchor, w: w || width, h: h || 200, vw, vh, dock }));
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} />

<div
  class="frame glass"
  class:ready={measured}
  {role}
  aria-label={label}
  use:glassShape={{ radius: 14 }}
  bind:offsetWidth={w}
  bind:offsetHeight={h}
  style:left="{placed.x}px"
  style:top="{placed.y}px"
  style:width="{width}px"
  style:max-height="calc(100vh - 16px)"
  style:transform-origin="{placed.originX}px {placed.originY}px"
>
  {@render children()}
</div>

<style>
  .frame {
    position: fixed; border-radius: 14px; padding: 6px; overflow: auto; box-shadow: 0 10px 30px rgba(0, 0, 0, 0.32), 0 1px 0 rgba(255, 255, 255, 0.14) inset;
    opacity: 0; scrollbar-width: thin;
  }
  .frame.ready { opacity: 1; animation: pop var(--dur) var(--ease-out); }
  @keyframes pop { from { opacity: 0; transform: scale(0.9); } to { opacity: 1; transform: scale(1); } }
</style>
