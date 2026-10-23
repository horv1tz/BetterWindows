<script lang="ts" generics="T extends string">
  import { getRowContext } from '../rowContext';

  interface Option { value: T; label: string; disabled?: boolean }
  let { options, value, onchange, ariaLabel, disabled = false, width }: {
    options: readonly Option[]; value: T; onchange: (v: T) => void; ariaLabel?: string; disabled?: boolean; width?: number;
  } = $props();
  const row = getRowContext();
  const off = $derived(disabled || !!row?.().disabled);
</script>

<span class="wrap" style:min-width={width ? `${width}px` : undefined}>
  <select
    {value} disabled={off} aria-label={ariaLabel} aria-labelledby={ariaLabel ? undefined : row?.().labelId} aria-describedby={row?.().descId}
    onchange={(e) => onchange(e.currentTarget.value as T)}
  >
    {#each options as o (o.value)}
      <option value={o.value} disabled={o.disabled} selected={o.value === value}>{o.label}</option>
    {/each}
  </select>
  <svg class="chev" width="10" height="10" viewBox="0 0 10 10" aria-hidden="true"><path d="M2 3.6 5 6.4 8 3.6" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" /></svg>
</span>

<style>
  .wrap { position: relative; display: inline-flex; max-width: 100%; }
  select {
    -webkit-appearance: none; appearance: none; width: 100%; height: 26px; padding: 0 28px 0 10px; border-radius: 6px;
    border: 0.5px solid var(--s-btn-border); background: var(--s-btn); box-shadow: var(--s-btn-shadow); text-overflow: ellipsis;
  }
  select:hover:not(:disabled) { background: var(--s-btn-hover); }
  select:disabled { opacity: 0.45; }
  .chev { position: absolute; right: 9px; top: 8px; pointer-events: none; color: var(--text-2); }
</style>
