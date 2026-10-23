<script lang="ts">
  import Calculator from '@lucide/svelte/icons/calculator';
  import type { SearchAction, SearchResult } from '$lib/protocol/generated';
  import { calcView } from './calcFormat';
  import { t } from './strings';

  let {
    result, selected, action, id, onrun, onhover,
  }: { result: SearchResult; selected: boolean; action: SearchAction | undefined; id: string; onrun: () => void; onhover: () => void } = $props();
  const view = $derived(calcView(result));
</script>

<!-- svelte-ignore a11y_click_events_have_key_events -->
<div {id} class="card" class:sel={selected} role="option" aria-selected={selected} tabindex="-1" onclick={onrun} onpointermove={onhover}>
  <div class="expr">
    <span class="kind"><Calculator size={13} strokeWidth={2} />{t('sp.group.calc')}</span>
    <span class="e" title={view.expression}>{view.expression}</span>
  </div>
  <div class="eq" aria-hidden="true">=</div>
  <div class="ans">
    <span class="v" aria-label={t('sp.calc.copy')}>{view.answer}</span>
    {#if selected && action}<span class="act"><kbd>↵</kbd>{action.label}</span>{/if}
  </div>
</div>

<style>
  .card {
    display: grid; grid-template-columns: minmax(0, 1fr) auto minmax(0, auto); align-items: center; gap: 14px;
    margin: 8px 8px 4px; padding: 14px 18px; min-height: 84px; border-radius: 13px;
    background: color-mix(in oklab, var(--text) 6%, transparent);
    border: 1px solid transparent; cursor: default;
    transition: background var(--dur-fast), border-color var(--dur-fast);
  }
  .card.sel {
    background: color-mix(in oklab, var(--accent) 18%, transparent);
    border-color: color-mix(in oklab, var(--accent) 65%, transparent);
  }
  .expr { display: flex; flex-direction: column; gap: 4px; min-width: 0; }
  .kind { display: inline-flex; align-items: center; gap: 5px; font-size: 11px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.06em; color: var(--text-2); }
  .e { font-size: 19px; color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; font-variant-numeric: tabular-nums; }
  .eq { font-size: 26px; font-weight: 300; color: var(--text-3); }
  .ans { display: flex; flex-direction: column; align-items: flex-end; gap: 2px; min-width: 0; }
  .v { font-size: 38px; font-weight: 500; letter-spacing: -0.02em; line-height: 1.1; font-variant-numeric: tabular-nums; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .act { display: inline-flex; align-items: center; gap: 6px; font-size: 12px; color: var(--text-2); }
  kbd { display: inline-grid; place-items: center; min-width: 20px; height: 20px; padding: 0 5px; border-radius: 5px; font: 600 11px/1 var(--font-ui); background: var(--hover); color: var(--text); }
</style>
