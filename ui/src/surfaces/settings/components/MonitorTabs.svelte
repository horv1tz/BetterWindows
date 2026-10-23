<script lang="ts">
  import type { MonitorInfo } from '$lib/protocol/generated';
  import { monitorAspect } from '../format';
  import { t } from '../i18n';
  import ScreenArt from '../art/ScreenArt.svelte';

  let { monitors, value, onchange, includeAll = false, ariaLabel }: {
    monitors: readonly MonitorInfo[]; value: string; onchange: (id: string) => void; includeAll?: boolean; ariaLabel?: string;
  } = $props();
  const tabs = $derived([...(includeAll ? [{ id: 'all', label: t('monitor.all'), sub: '', m: undefined as MonitorInfo | undefined }] : []),
    ...monitors.map((m, i) => ({ id: m.id, label: m.name || t('monitor.n', { n: i + 1 }), sub: m.primary ? t('monitor.primary') : '', m: m as MonitorInfo | undefined }))]);
  let host: HTMLElement | undefined = $state();

  function key(e: KeyboardEvent) {
    const d = e.key === 'ArrowRight' || e.key === 'ArrowDown' ? 1 : e.key === 'ArrowLeft' || e.key === 'ArrowUp' ? -1 : 0;
    if (!d) return;
    e.preventDefault();
    const i = Math.max(0, tabs.findIndex((x) => x.id === value));
    const next = (i + d + tabs.length) % tabs.length;
    onchange(tabs[next].id);
    queueMicrotask(() => host?.querySelectorAll<HTMLElement>('[role=radio]')[next]?.focus());
  }
</script>

<div class="tabs" role="radiogroup" aria-label={ariaLabel ?? t('monitor.pick')} bind:this={host}>
  {#each tabs as tab (tab.id)}
    <button type="button" role="radio" class="tab" class:sel={tab.id === value} aria-checked={tab.id === value} tabindex={tab.id === value ? 0 : -1} onclick={() => onchange(tab.id)} onkeydown={key}>
      <span class="art">
        {#if tab.m}
          <ScreenArt width={Math.round(Math.min(64, 40 * monitorAspect(tab.m)))} aspect={monitorAspect(tab.m)} menubar={false} />
        {:else}
          <span class="pair"><ScreenArt width={34} aspect={1.6} menubar={false} /><ScreenArt width={34} aspect={1.6} menubar={false} /></span>
        {/if}
      </span>
      <span class="name">{tab.label}</span>
      {#if tab.sub}<span class="sub">{tab.sub}</span>{/if}
    </button>
  {/each}
</div>

<style>
  .tabs { display: flex; flex-wrap: wrap; gap: 8px; }
  .tab {
    display: flex; flex-direction: column; align-items: center; gap: 2px; min-width: 88px; padding: 8px 10px 7px; border-radius: 10px; border: 0.5px solid transparent;
    background: none; color: var(--text);
  }
  .tab:hover:not(.sel) { background: var(--hover); }
  .tab.sel { background: color-mix(in srgb, var(--accent) 14%, transparent); border-color: color-mix(in srgb, var(--accent) 55%, transparent); }
  .art { height: 44px; display: grid; place-items: center; margin-bottom: 3px; }
  .pair { display: flex; gap: 5px; }
  .name { font-size: 12px; font-weight: 500; max-width: 130px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .sub { font-size: 10.5px; color: var(--text-2); margin-top: -1px; }
</style>
