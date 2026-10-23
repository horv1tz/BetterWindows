<script lang="ts">
  import type { SearchAction } from '$lib/protocol/generated';
  import type { Entry } from './viewModel';
  import ResultIcon from './ResultIcon.svelte';
  import Highlight from './Highlight.svelte';
  import { t } from './strings';

  let {
    entry, query, selected, action, confirming, id, onrun, onhover, intro = false,
  }: {
    entry: Entry; query: string; selected: boolean; action: SearchAction | undefined; confirming: boolean; id: string;
    onrun: () => void; onhover: (e: PointerEvent) => void; intro?: boolean;
  } = $props();
  const r = $derived(entry.result);
  const quick = $derived(entry.index < 9 ? t('sp.quick', { n: entry.index + 1 }) : '');
</script>

<!-- svelte-ignore a11y_click_events_have_key_events -->
<div
  {id}
  class="row"
  class:sel={selected}
  class:confirm={selected && confirming}
  class:intro
  style:--i={Math.min(entry.index, 8)}
  role="option"
  aria-selected={selected}
  tabindex="-1"
  onclick={onrun}
  onpointermove={onhover}
>
  <span class="ico"><ResultIcon result={r} size={30} /></span>
  <span class="txt">
    <span class="title"><Highlight text={r.title} {query} /></span>
    {#if selected && confirming}<span class="sub warn">{t('sp.hint.confirm')}</span>
    {:else if r.subtitle}<span class="sub">{r.subtitle}</span>{/if}
  </span>
  <span class="hint" aria-hidden={!selected}>
    {#if selected && confirming}
      <kbd>↵</kbd><span class="label">{t('sp.hint.confirmShort')}</span>
    {:else if selected && action}
      <kbd>↵</kbd><span class="label">{action.label}</span>
    {:else if quick}
      <span class="quick">{quick}</span>
    {/if}
  </span>
</div>

<style>
  .row {
    display: flex; align-items: center; gap: 11px;
    min-height: 46px; padding: 6px 10px; margin: 0 6px;
    border-radius: 9px; color: var(--text); cursor: default;
    contain: layout paint;
  }
  .row.intro { animation: row-in 200ms var(--ease-out) both; animation-delay: calc(var(--i) * 14ms); }
  @keyframes row-in { from { opacity: 0; transform: translateY(4px); } to { opacity: 1; transform: none; } }
  .ico { display: grid; place-items: center; width: 30px; height: 30px; flex: none; }
  .txt { display: flex; flex-direction: column; min-width: 0; flex: 1; gap: 1px; }
  .title { font-size: 14px; font-weight: 440; line-height: 1.25; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; letter-spacing: -0.003em; }
  .sub { font-size: 12px; line-height: 1.25; color: var(--text-2); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .hint { display: flex; align-items: center; gap: 6px; flex: none; font-size: 12px; color: var(--text-2); max-width: 46%; }
  .label { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .quick { font-size: 11px; color: var(--text-3); font-variant-numeric: tabular-nums; }
  kbd {
    display: inline-grid; place-items: center; min-width: 20px; height: 20px; padding: 0 5px; border-radius: 5px;
    font: 600 11px/1 var(--font-ui); background: rgba(255, 255, 255, 0.24); color: inherit;
  }

  .row.sel { background: var(--accent); color: #fff; }
  .row.sel .sub, .row.sel .hint { color: rgba(255, 255, 255, 0.78); }
  .row.sel.confirm { background: var(--sp-danger, #e5382f); }
  .sub.warn { font-weight: 600; color: #fff; }
  .row.sel.confirm .sub, .row.sel.confirm .hint { color: #fff; }
  :global(:root[data-reduce-motion='true']) .row.intro { animation: none; }
</style>
