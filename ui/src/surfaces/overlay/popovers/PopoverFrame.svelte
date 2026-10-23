<script lang="ts">
  // FROZEN (scaffold). Positions a glass panel next to an anchor rect, clamped to the viewport, with an open animation.
  import type { Snippet } from 'svelte';
  import type { Rect } from '$lib/protocol/generated';
  import { glassShape } from '$lib/shape';

  type Placement = 'below-start' | 'below-end' | 'below-center' | 'above-start' | 'above-center' | 'above-end' | 'auto';
  let {
    anchor,
    placement = 'auto',
    width,
    gap = 6,
    margin = 8,
    label = '',
    children,
  }: { anchor: Rect; placement?: Placement; width?: number; gap?: number; margin?: number; label?: string; children: Snippet } = $props();

  let panelW = $state(280); // real size arrives via bind:offsetWidth
  let panelH = $state(200);
  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);

  const resolved = $derived.by(() => {
    if (placement !== 'auto') return placement;
    // menu-bar items (top half) open downwards, dock items (bottom half) upwards
    return anchor.y + anchor.height / 2 < vh / 2 ? 'below-start' : 'above-center';
  });
  const pos = $derived.by(() => {
    const [v, h] = resolved.split('-') as ['below' | 'above', 'start' | 'center' | 'end'];
    let x = h === 'start' ? anchor.x : h === 'end' ? anchor.x + anchor.width - panelW : anchor.x + anchor.width / 2 - panelW / 2;
    let y = v === 'below' ? anchor.y + anchor.height + gap : anchor.y - gap - panelH;
    x = Math.min(Math.max(margin, x), Math.max(margin, vw - panelW - margin));
    y = Math.min(Math.max(margin, y), Math.max(margin, vh - panelH - margin));
    return { x, y, origin: `${v === 'below' ? 'top' : 'bottom'} ${h === 'start' ? 'left' : h === 'end' ? 'right' : 'center'}` };
  });
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} />

<div
  class="frame glass"
  role="dialog"
  aria-label={label}
  use:glassShape={{ radius: 12 }}
  bind:offsetWidth={panelW}
  bind:offsetHeight={panelH}
  style:left="{pos.x}px"
  style:top="{pos.y}px"
  style:width={width ? `${width}px` : undefined}
  style:transform-origin={pos.origin}
>
  {@render children()}
</div>

<style>
  .frame {
    position: fixed;
    border-radius: 12px;
    padding: 6px;
    min-width: 160px;
    max-height: calc(100vh - 16px);
    overflow: auto;
    animation: pop var(--dur) var(--ease-out);
    box-shadow: var(--shadow-lg);
  }
  @keyframes pop {
    from { opacity: 0; transform: scale(0.96); }
    to { opacity: 1; transform: scale(1); }
  }
</style>
