<script lang="ts">
  import { getRowContext } from '../rowContext';

  let {
    value, min, max, step = 1, oninput, onchange, label, format = (v: number) => String(v), disabled = false, showValue = true, width = 190,
  }: {
    value: number; min: number; max: number; step?: number; oninput?: (v: number) => void; onchange: (v: number) => void;
    label?: string; format?: (v: number) => string; disabled?: boolean; showValue?: boolean; width?: number;
  } = $props();
  const row = getRowContext();
  /** value while the thumb is dragged (the setting itself is only written on release) */
  let live = $state<number | null>(null);
  const shown = $derived(live ?? value);
  const pct = $derived(max > min ? ((shown - min) / (max - min)) * 100 : 0);
  const off = $derived(disabled || !!row?.().disabled);
</script>

<div class="slider" style:--w="{width}px">
  <input
    type="range" {min} {max} {step} value={shown} disabled={off} style:--p="{pct}%"
    aria-label={label} aria-labelledby={label ? undefined : row?.().labelId} aria-describedby={row?.().descId}
    aria-valuetext={format(shown)}
    oninput={(e) => { live = Number(e.currentTarget.value); oninput?.(live); }}
    onchange={(e) => { const v = Number(e.currentTarget.value); live = null; onchange(v); }}
    onblur={() => { live = null; }}
  />
  {#if showValue}<output class="val">{format(shown)}</output>{/if}
</div>

<style>
  .slider { display: inline-flex; align-items: center; gap: 12px; }
  input[type='range'] {
    -webkit-appearance: none; appearance: none; width: var(--w); height: 22px; margin: 0; background: none; cursor: default;
  }
  input[type='range']::-webkit-slider-runnable-track {
    height: 4px; border-radius: 2px;
    background: linear-gradient(to right, var(--accent) var(--p), var(--s-track) var(--p));
  }
  input[type='range']::-webkit-slider-thumb {
    -webkit-appearance: none; width: 18px; height: 18px; margin-top: -7px; border-radius: 50%; background: #fff;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.35), 0 0 0 0.5px rgba(0, 0, 0, 0.1);
    transition: transform var(--dur-fast) var(--ease-out);
  }
  input[type='range']:active::-webkit-slider-thumb { transform: scale(1.08); }
  input[type='range']:focus-visible { outline: none; }
  input[type='range']:focus-visible::-webkit-slider-thumb { box-shadow: 0 0 0 3px var(--focus), 0 1px 3px rgba(0, 0, 0, 0.35); }
  input[type='range']:disabled { opacity: 0.45; }
  .val { min-width: 48px; text-align: right; font-variant-numeric: tabular-nums; color: var(--text-2); }
</style>
