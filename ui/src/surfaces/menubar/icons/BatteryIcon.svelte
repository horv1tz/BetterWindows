<script lang="ts">
  import type { BatteryVisual } from '../battery';

  let { visual }: { visual: BatteryVisual } = $props();

  // Cell geometry (viewBox 26 x 12): body 0.6..22.4, inner cell 2.2..20.8 (18.6 wide).
  const CELL_X = 2.3;
  const CELL_W = 18.4;
  const fillW = $derived(visual.fill * CELL_W);
</script>

<svg class="battery" data-tone={visual.tone} width="26" height="12" viewBox="0 0 26 12" fill="none" aria-hidden="true" focusable="false">
  <rect class="body" x="0.65" y="0.65" width="21.7" height="10.7" rx="3.2" stroke="currentColor" stroke-width="1.1" />
  <path class="cap" d="M23.6 4.1c.9.3 1.5 1 1.5 1.9s-.6 1.6-1.5 1.9z" fill="currentColor" />
  {#if fillW > 0}
    <rect class="cell" x={CELL_X} y="2.3" width={fillW} height="7.4" rx="1.7" />
  {/if}
  {#if visual.bolt}
    <path class="bolt" d="M12.9 1.6 7.4 6.6h3.5L9.7 10.4l5.6-5.2h-3.6z" />
  {:else if visual.plugged}
    <g class="plug" stroke-linecap="round">
      <path d="M10 3.4v1.6M14 3.4v1.6" stroke-width="1.2" />
      <path d="M8.9 5h6.2v1.2a3.1 3.1 0 0 1-6.2 0z" stroke-width="0.9" />
    </g>
  {/if}
</svg>

<style>
  .battery {
    display: block;
    overflow: visible;
    --bw-warn: #ff9f0a;
    --bw-danger: #ff453a;
    --bw-good: #30d158;
    --on-cell: rgba(18, 18, 22, 0.78);
  }
  :global(:root[data-theme='light']) .battery {
    --bw-warn: #e58a00;
    --bw-danger: #e5322b;
    --bw-good: #1fae4a;
    --on-cell: rgba(255, 255, 255, 0.96);
  }
  .body { opacity: 0.5; }
  .cap { opacity: 0.45; }
  .cell { fill: var(--cell, currentColor); }

  .battery[data-tone='low'] { --cell: var(--bw-warn); }
  .battery[data-tone='critical'] { --cell: var(--bw-danger); }
  .battery[data-tone='charging'] { --cell: var(--bw-good); }

  /* The bolt sits on the green cell: white with a dark keyline reads on every wallpaper. */
  .bolt { fill: #fff; stroke: rgba(0, 0, 0, 0.55); stroke-width: 0.8; stroke-linejoin: round; paint-order: stroke; }
  /* The plug sits on a currentColor cell, so it takes the inverse colour. */
  .plug { stroke: var(--on-cell); fill: var(--on-cell); }
</style>
