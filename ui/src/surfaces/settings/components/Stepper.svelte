<script lang="ts">
  import { Minus, Plus } from '@lucide/svelte';
  import { getRowContext } from '../rowContext';
  import { clamp } from '../validate';
  import { t } from '../i18n';

  let { value, min, max, step = 1, onchange, label, disabled = false }: {
    value: number; min: number; max: number; step?: number; onchange: (v: number) => void; label?: string; disabled?: boolean;
  } = $props();
  const row = getRowContext();
  const off = $derived(disabled || !!row?.().disabled);
  const set = (v: number) => { const c = clamp(v, min, max); if (c !== value) onchange(c); };
  function key(e: KeyboardEvent) {
    const d = e.key === 'ArrowUp' || e.key === 'ArrowRight' ? 1 : e.key === 'ArrowDown' || e.key === 'ArrowLeft' ? -1 : 0;
    if (d) { e.preventDefault(); set(value + d * step * (e.shiftKey ? 5 : 1)); }
    else if (e.key === 'Home') { e.preventDefault(); set(min); }
    else if (e.key === 'End') { e.preventDefault(); set(max); }
    else if (e.key === 'PageUp') { e.preventDefault(); set(value + step * 5); }
    else if (e.key === 'PageDown') { e.preventDefault(); set(value - step * 5); }
  }
</script>

<div class="stepper">
  <div
    class="val" role="spinbutton" tabindex={off ? -1 : 0} aria-valuenow={value} aria-valuemin={min} aria-valuemax={max}
    aria-label={label} aria-labelledby={label ? undefined : row?.().labelId} aria-disabled={off} onkeydown={key}
  >{value}</div>
  <div class="btns">
    <button type="button" tabindex="-1" aria-label={t('common.decrease')} disabled={off || value <= min} onclick={() => set(value - step)}><Minus size={13} strokeWidth={2.4} /></button>
    <button type="button" tabindex="-1" aria-label={t('common.increase')} disabled={off || value >= max} onclick={() => set(value + step)}><Plus size={13} strokeWidth={2.4} /></button>
  </div>
</div>

<style>
  .stepper { display: inline-flex; align-items: center; gap: 6px; }
  .val {
    min-width: 46px; height: 26px; display: grid; place-items: center; padding: 0 10px; border-radius: 6px; font-variant-numeric: tabular-nums;
    background: var(--s-field); border: 0.5px solid var(--s-field-border);
  }
  .val[aria-disabled='true'] { opacity: 0.45; }
  .btns { display: inline-flex; border-radius: 6px; overflow: hidden; border: 0.5px solid var(--s-btn-border); box-shadow: var(--s-btn-shadow); background: var(--s-btn); }
  .btns button { width: 26px; height: 24px; display: grid; place-items: center; border: 0; background: none; padding: 0; }
  .btns button + button { border-left: 0.5px solid var(--s-btn-border); }
  .btns button:hover:not(:disabled) { background: var(--s-btn-hover); }
  .btns button:active:not(:disabled) { background: var(--press); }
  .btns button:disabled { opacity: 0.35; }
</style>
