<script lang="ts">
  import { onMount } from 'svelte';
  import { getBridge, getInit } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import X from '@lucide/svelte/icons/x';
  import ShieldAlert from '@lucide/svelte/icons/shield-alert';
  import type { PopoverRequest, WindowRef } from '$lib/protocol/generated';
  import DockFrame from './dock/DockFrame.svelte';
  import { fire } from './dock/calls';
  import { t, windowsCount } from './dock/strings';
  import { useDockItem } from './dock/useDockState.svelte';

  /** Window list of an app with several windows (click the dock icon): thumbnails when the host provides them, else a list with large icons. */
  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bridge = getBridge();
  const dock = useDockItem(() => request.itemId);
  const settings = hostState('settings.changed', getInit().settings);
  const item = $derived(dock.item);
  const windows = $derived(item?.windows ?? []);
  const side = $derived(settings.value.dock.position);
  const grid = $derived(windows.some((w) => w.thumbUrl));
  const cols = $derived(grid ? (windows.length <= 2 ? Math.max(1, windows.length) : windows.length <= 4 ? 2 : 3) : 1);
  const CARD = 212;
  const panelWidth = $derived(grid ? cols * CARD + (cols - 1) * 8 + 12 : 304);
  let root = $state<HTMLElement>();

  $effect(() => () => { dock.dispose(); settings.dispose(); });
  onMount(() => (root?.querySelector<HTMLElement>('[data-card]:not([aria-disabled])') ?? root)?.focus({ preventScroll: true }));
  // nothing left to list (every window closed, app quit)
  $effect(() => { if (dock.loaded && (!item || windows.length === 0)) onclose(); });

  function activate(w: WindowRef) {
    if (w.elevated) return; // the shell cannot control elevated windows (UIPI)
    onclose(); // hide the overlay first so the activated window is not pushed back by it
    void fire(bridge.call('dock.activateWindow', { handle: w.handle }), 'activate window');
  }
  function close(w: WindowRef) {
    if (w.elevated) return;
    void fire(bridge.call('dock.closeWindow', { handle: w.handle }), 'close window');
  }
  function onAux(e: MouseEvent, w: WindowRef) {
    if (e.button !== 1) return;
    e.preventDefault();
    close(w);
  }

  function onKey(e: KeyboardEvent) {
    const cards = [...(root?.querySelectorAll<HTMLElement>('[data-card]') ?? [])];
    if (!cards.length) return;
    const at = cards.indexOf(document.activeElement as HTMLElement);
    const go = (i: number) => { cards[Math.min(cards.length - 1, Math.max(0, i))]?.focus(); e.preventDefault(); };
    switch (e.key) {
      case 'ArrowRight': return go(at + 1);
      case 'ArrowLeft': return go(at - 1);
      case 'ArrowDown': return go(at < 0 ? 0 : at + cols);
      case 'ArrowUp': return go(at < 0 ? cards.length - 1 : at - cols);
      case 'Home': return go(0);
      case 'End': return go(cards.length - 1);
      case 'Delete': case 'Backspace': {
        if (at < 0) return;
        e.preventDefault();
        const w = windows.find((x) => x.handle === cards[at].dataset.card);
        if (w) { close(w); go(at + 1 < cards.length ? at + 1 : at - 1); }
      }
    }
  }
</script>

{#if item && windows.length}
  <DockFrame anchor={request.anchor} dock={side} width={panelWidth} label={t('wl.label', { app: item.name })} role="dialog">
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <div class="wl" bind:this={root} tabindex="-1" role="presentation" onkeydown={onKey}>
      <header class="head">
        <AppIcon src={item.iconUrl} name={item.name} size={20} />
        <span class="app">{item.name}</span>
        <span class="count">{windowsCount(windows.length)}</span>
      </header>
      <div class="items" class:grid style:grid-template-columns={grid ? `repeat(${cols}, ${CARD}px)` : undefined}>
        {#each windows as w (w.handle)}
          <div class="card" class:active={w.active} class:dim={w.minimized || w.elevated} class:list={!grid}>
            <button
              type="button" class="main" data-card={w.handle} aria-disabled={w.elevated || undefined} aria-keyshortcuts="Delete"
              aria-label={`${w.title || t('wl.untitled')}${w.minimized ? `, ${t('menu.minimized')}` : ''}${w.elevated ? `, ${t('menu.elevated')}` : ''}`}
              title={w.elevated ? t('menu.elevated') : w.title}
              onclick={() => activate(w)}
              onauxclick={(e) => onAux(e, w)}
              onmousedown={(e) => { if (e.button === 1) e.preventDefault(); }}
            >
              {#if grid}
                <span class="thumb">
                  {#if w.thumbUrl}<img src={w.thumbUrl} alt="" draggable="false" />{:else}<span class="ph"><AppIcon src={w.iconUrl} name={w.title} size={44} /></span>{/if}
                  {#if w.minimized}<span class="chip">{t('menu.minimized')}</span>{/if}
                </span>
                <span class="cap">
                  <AppIcon src={w.iconUrl} name={w.title} size={16} radius={0.2} />
                  <span class="title">{w.title || t('wl.untitled')}</span>
                  {#if w.elevated}<span class="shield" title={t('menu.elevated')}><ShieldAlert size={14} aria-label={t('menu.elevated')} /></span>{/if}
                </span>
              {:else}
                <span class="big"><AppIcon src={w.iconUrl} name={w.title} size={32} /></span>
                <span class="texts">
                  <span class="title">{w.title || t('wl.untitled')}</span>
                  {#if w.elevated}<span class="subtitle warn"><ShieldAlert size={12} aria-hidden="true" /> {t('menu.elevated')}</span>
                  {:else if w.minimized}<span class="subtitle">{t('menu.minimized')}</span>{/if}
                </span>
              {/if}
            </button>
            {#if !w.elevated}
              <button type="button" class="x" tabindex="-1" aria-label={t('menu.close')} title={t('menu.close')} onclick={() => close(w)}><X size={13} strokeWidth={2.4} /></button>
            {/if}
          </div>
        {/each}
      </div>
    </div>
  </DockFrame>
{/if}

<style>
  .wl { outline: none; }
  .head { display: flex; align-items: center; gap: 8px; padding: 4px 6px 8px; }
  .app { font-weight: 600; font-size: 13px; }
  .count { color: var(--text-2); font-size: 12px; margin-left: auto; }
  .items { display: flex; flex-direction: column; gap: 2px; max-height: calc(100vh - 110px); overflow: auto; scrollbar-width: thin; }
  .items.grid { display: grid; gap: 8px; }

  .card { position: relative; border-radius: 10px; }
  .main {
    all: unset; box-sizing: border-box; display: flex; width: 100%; border-radius: 10px; cursor: default; color: var(--text);
  }
  .card.list .main { align-items: center; gap: 10px; height: 48px; padding: 0 34px 0 8px; }
  .card:not(.list) .main { flex-direction: column; padding: 6px; gap: 6px; }
  .card:hover .main:not([aria-disabled='true']), .main:focus-visible { background: var(--hover); }
  .main:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  .card.active .main { background: color-mix(in srgb, var(--accent) 16%, transparent); box-shadow: 0 0 0 1.5px color-mix(in srgb, var(--accent) 70%, transparent) inset; }
  .card.dim .main { opacity: 0.8; }
  .main[aria-disabled='true'] { opacity: 0.6; }

  .thumb {
    position: relative; display: block; width: 100%; aspect-ratio: 16 / 10; border-radius: 7px; overflow: hidden;
    background: color-mix(in srgb, var(--text) 8%, transparent); border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-sm);
  }
  .thumb img { display: block; width: 100%; height: 100%; object-fit: cover; }
  .ph { position: absolute; inset: 0; display: grid; place-items: center; }
  .chip { position: absolute; left: 6px; bottom: 6px; padding: 1px 7px; border-radius: 999px; font-size: 10.5px; background: rgba(0, 0, 0, 0.55); color: #fff; backdrop-filter: blur(6px); }
  .cap { display: flex; align-items: center; gap: 6px; min-width: 0; padding: 0 2px 1px; font-size: 12.5px; }
  .title { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
  .shield { color: var(--text-2); display: grid; }
  .big { flex: none; display: grid; }
  .texts { display: flex; flex-direction: column; min-width: 0; flex: 1; font-size: 13px; }
  .subtitle { font-size: 11.5px; color: var(--text-2); display: flex; align-items: center; gap: 4px; }
  .subtitle.warn { color: color-mix(in srgb, #ff9f0a 80%, var(--text)); }

  .x {
    all: unset; box-sizing: border-box; position: absolute; width: 22px; height: 22px; border-radius: 50%; display: grid; place-items: center; cursor: default;
    background: rgba(30, 30, 34, 0.72); color: #fff; opacity: 0; transition: opacity var(--dur-fast) var(--ease-out); box-shadow: 0 1px 4px rgba(0, 0, 0, 0.3);
  }
  .card:not(.list) .x { top: 10px; right: 10px; }
  .card.list .x { right: 10px; top: 50%; margin-top: -11px; }
  .card:hover .x, .card:focus-within .x { opacity: 1; }
  .x:hover { background: rgba(0, 0, 0, 0.9); }
  .x:focus-visible { outline: 2px solid #fff; outline-offset: -2px; }
</style>
