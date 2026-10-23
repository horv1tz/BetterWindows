<script lang="ts">
  import type { Snippet } from 'svelte';
  import { has, t } from '../i18n';
  import { nav } from '../nav.svelte';
  import { nextId, setRowContext } from '../rowContext';

  let {
    sid, label, desc, children, disabled = false, stack = false, lead, below,
  }: {
    /** stable id: label/description/keywords come from strings `<sid>.label|desc|kw` and the settings search can jump to it */
    sid?: string; label?: string; desc?: string; children?: Snippet; disabled?: boolean;
    /** control below the text instead of beside it */
    stack?: boolean; lead?: Snippet; below?: Snippet;
  } = $props();

  const labelId = nextId('lbl');
  const descId = nextId('dsc');
  const text = $derived(label ?? (sid ? t(`${sid}.label`) : ''));
  const description = $derived(desc ?? (sid && has(`${sid}.desc`) ? t(`${sid}.desc`) : undefined));
  setRowContext(() => ({ labelId, descId: description ? descId : undefined, disabled }));

  let el: HTMLElement | undefined = $state();
  const flash = $derived(!!sid && nav.highlight === sid);
  $effect(() => {
    if (flash && el) el.scrollIntoView({ block: 'center', behavior: document.documentElement.dataset.reduceMotion === 'true' ? 'auto' : 'smooth' });
  });
</script>

<div class="row" class:stack class:flash class:disabled data-sid={sid} bind:this={el}>
  {#if lead}<div class="lead">{@render lead()}</div>{/if}
  <div class="text">
    <div class="label" id={labelId}>{text}</div>
    {#if description}<div class="desc" id={descId}>{description}</div>{/if}
  </div>
  {#if children}<div class="ctl">{@render children()}</div>{/if}
  {#if below}<div class="below">{@render below()}</div>{/if}
</div>

<style>
  .row { position: relative; display: flex; align-items: center; gap: 16px; min-height: 44px; padding: 9px 14px; }
  .row.flash::after {
    content: ''; position: absolute; inset: 0; background: var(--accent); pointer-events: none; opacity: 0; animation: flash 1.6s var(--ease-out) 1;
  }
  @keyframes flash { 0%, 100% { opacity: 0; } 18% { opacity: 0.2; } 60% { opacity: 0.12; } }
  .lead { flex: none; display: flex; align-items: center; }
  .text { flex: 1 1 auto; min-width: 0; }
  .label { font-weight: 500; }
  .desc { margin-top: 1px; color: var(--text-2); font-size: 12px; line-height: 1.35; text-wrap: pretty; }
  .ctl { flex: none; display: flex; align-items: center; justify-content: flex-end; gap: 8px; max-width: 60%; }
  .below { flex-basis: 100%; }
  .disabled .text { opacity: 0.5; }
  .stack { flex-wrap: wrap; row-gap: 10px; }
  .stack .ctl { flex-basis: 100%; max-width: none; justify-content: flex-start; }
  @container (max-width: 560px) {
    .row:not(.stack) .ctl { max-width: 52%; }
  }
</style>
