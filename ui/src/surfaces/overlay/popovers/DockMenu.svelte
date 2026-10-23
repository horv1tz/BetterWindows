<script lang="ts">
  import { onMount } from 'svelte';
  import { getBridge, getInit } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import Check from '@lucide/svelte/icons/check';
  import X from '@lucide/svelte/icons/x';
  import ShieldAlert from '@lucide/svelte/icons/shield-alert';
  import type { PopoverRequest, WindowRef } from '$lib/protocol/generated';
  import DockFrame from './dock/DockFrame.svelte';
  import { fire } from './dock/calls';
  import { buildMenu, type MenuEntry } from './dock/menuModel';
  import { t, windowsCount } from './dock/strings';
  import { useDockItem } from './dock/useDockState.svelte';

  /** Dock context menu (right-click, long-press or the menu key on a dock icon): windows, options, app actions, dock settings. */
  let { request, onclose }: { request: PopoverRequest; onclose: () => void } = $props();

  const bridge = getBridge();
  const dock = useDockItem(() => request.itemId);
  const settings = hostState('settings.changed', getInit().settings);
  const item = $derived(dock.item);
  const model = $derived(item ? buildMenu(item) : null);
  const side = $derived(settings.value.dock.position);
  let menuEl = $state<HTMLElement>();

  $effect(() => () => { dock.dispose(); settings.dispose(); });
  onMount(() => menuEl?.focus({ preventScroll: true }));
  // the item vanished while the menu was open (app quit, unpinned elsewhere): nothing left to act on
  $effect(() => { if (dock.loaded && !item) onclose(); });

  const subtitle = $derived.by(() => {
    if (!item || !model) return '';
    if (model.subtitle === 'running') return item.windows.length > 1 ? windowsCount(item.windows.length) : t('menu.running');
    return t(`menu.${model.subtitle}`);
  });

  /** Closes the overlay first (so the target app can take the foreground back), then fires the call. */
  function act(call: () => Promise<unknown>, what: string) {
    onclose();
    void fire(call(), what);
  }
  function run(e: MenuEntry) {
    if (!e.enabled || !item) return;
    const id = item.id;
    switch (e.action) {
      case 'open': return act(() => bridge.call('dock.activate', { itemId: id }), 'activate');
      case 'pin': return act(() => bridge.call('dock.pin', { appId: item.appId ?? id }), 'pin');
      case 'unpin': return act(() => bridge.call('dock.unpin', { itemId: id }), 'unpin');
      case 'reveal': return act(() => bridge.call('dock.revealInExplorer', { itemId: id }), 'reveal in explorer');
      case 'newWindow': return act(() => bridge.call('dock.newWindow', { itemId: id }), 'new window');
      case 'hide': return act(() => bridge.call('dock.hide', { itemId: id }), 'hide');
      case 'quit': return act(() => bridge.call('dock.quit', { itemId: id }), 'quit');
      case 'settings': return act(() => bridge.call('settings.openWindow', { page: 'dock' }), 'open dock settings');
    }
  }
  function activateWindow(w: WindowRef) {
    if (w.elevated) return; // the shell cannot control elevated windows (UIPI); the row explains why
    act(() => bridge.call('dock.activateWindow', { handle: w.handle }), 'activate window');
  }
  function closeWindow(w: WindowRef) {
    if (w.elevated) return;
    void fire(bridge.call('dock.closeWindow', { handle: w.handle }), 'close window');
  }

  // ---- keyboard: arrows / Home / End / type-ahead, Delete closes the focused window, Enter activates (native button click)
  const rows = () => [...(menuEl?.querySelectorAll<HTMLElement>('[data-row]') ?? [])];
  let typed = '';
  let typedTimer: ReturnType<typeof setTimeout> | undefined;
  function onKey(e: KeyboardEvent) {
    const list = rows();
    if (!list.length) return;
    const at = list.indexOf(document.activeElement as HTMLElement);
    const go = (i: number) => { list[(i + list.length) % list.length]?.focus(); e.preventDefault(); };
    if (e.key === 'ArrowDown') go(at < 0 ? 0 : at + 1);
    else if (e.key === 'ArrowUp') go(at < 0 ? list.length - 1 : at - 1);
    else if (e.key === 'Home') go(0);
    else if (e.key === 'End') go(list.length - 1);
    else if ((e.key === 'Delete' || e.key === 'Backspace') && at >= 0 && list[at].dataset.window) {
      e.preventDefault();
      const w = item?.windows.find((x) => x.handle === list[at].dataset.window);
      if (w) closeWindow(w);
    } else if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      typed = (typed + e.key).toLowerCase();
      clearTimeout(typedTimer);
      typedTimer = setTimeout(() => (typed = ''), 700);
      const from = typed.length === 1 ? at + 1 : at;
      for (let k = 0; k < list.length; k++) {
        const el = list[(from + k + list.length) % list.length];
        if ((el.dataset.label ?? '').toLowerCase().startsWith(typed)) { el.focus(); e.preventDefault(); break; }
      }
    }
  }
</script>

{#if item && model}
  <DockFrame anchor={request.anchor} dock={side} width={272} label={t('menu.label')} role="menu">
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <div class="menu" bind:this={menuEl} tabindex="-1" role="presentation" onkeydown={onKey}>
      <header class="head">
        <AppIcon src={item.iconUrl} name={item.name} size={32} />
        <div class="titles">
          <div class="name">{item.name}</div>
          <div class="sub">{subtitle}</div>
        </div>
      </header>

      {#if model.windows.length > 0}
        <div class="label">{t('menu.windows')}</div>
        <div class="wins">
          {#each model.windows as w (w.handle)}
            <div class="wrow" class:dim={w.minimized || w.elevated}>
              <button
                type="button" class="row" role="menuitem" data-row data-window={w.handle} data-label={w.title}
                aria-disabled={w.elevated || undefined} aria-keyshortcuts="Delete"
                title={w.elevated ? t('menu.elevated') : w.title}
                onclick={() => activateWindow(w)}
              >
                <span class="lead">{#if w.active}<Check size={14} strokeWidth={2.4} aria-label={t('menu.active')} />{/if}</span>
                <span class="wicon"><AppIcon src={w.iconUrl} name={w.title} size={16} radius={0.2} /></span>
                <span class="wtitle">{w.title || t('wl.untitled')}</span>
                {#if w.elevated}
                  <span class="tail" title={t('menu.elevated')}><ShieldAlert size={14} aria-label={t('menu.elevated')} /></span>
                {:else if w.minimized}
                  <span class="tail muted">{t('menu.minimized')}</span>
                {/if}
              </button>
              {#if !w.elevated}
                <button type="button" class="wclose" tabindex="-1" aria-label={t('menu.close')} title={t('menu.close')} onclick={() => closeWindow(w)}><X size={13} strokeWidth={2.4} /></button>
              {/if}
            </div>
          {/each}
        </div>
        <div class="sep" role="separator"></div>
      {/if}

      {#snippet entry(e: MenuEntry)}
        <button type="button" class="row" class:danger={e.danger} role="menuitem" data-row={e.enabled || undefined} data-label={t(e.label)} aria-disabled={!e.enabled || undefined} onclick={() => run(e)}>
          <span class="etext">{t(e.label)}</span>
        </button>
      {/snippet}

      {#if model.primary.length}
        {#each model.primary as e (e.action)}{@render entry(e)}{/each}
        <div class="sep" role="separator"></div>
      {/if}

      <div class="label">{t('menu.options')}</div>
      {#each model.options as e (e.action)}{@render entry(e)}{/each}

      {#if model.app.length}
        <div class="sep" role="separator"></div>
        {#each model.app as e (e.action)}{@render entry(e)}{/each}
      {/if}

      <div class="sep" role="separator"></div>
      {#each model.footer as e (e.action)}{@render entry(e)}{/each}
    </div>
  </DockFrame>
{/if}

<style>
  .menu { outline: none; display: flex; flex-direction: column; }
  .head { display: flex; align-items: center; gap: 10px; padding: 6px 8px 8px; }
  .titles { min-width: 0; }
  .name { font-weight: 600; font-size: 13.5px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .sub { font-size: 11.5px; color: var(--text-2); }
  .label { padding: 7px 10px 3px; font-size: 11px; font-weight: 600; letter-spacing: 0.02em; color: var(--text-2); }
  .sep { height: 1px; margin: 5px 8px; background: var(--separator); }
  .wins { display: flex; flex-direction: column; max-height: 232px; overflow: auto; scrollbar-width: thin; }

  .row {
    all: unset; box-sizing: border-box; display: flex; align-items: center; gap: 6px; width: 100%; height: 28px; padding: 0 10px; border-radius: 6px;
    font-size: 13px; cursor: default; color: var(--text); white-space: nowrap;
  }
  .row:hover:not([aria-disabled='true']), .row:focus-visible { background: var(--accent); color: #fff; }
  .row:focus-visible { outline: none; }
  .row[aria-disabled='true'] { color: var(--text-3); }
  .row.danger:not([aria-disabled='true']):not(:hover):not(:focus-visible) { color: color-mix(in srgb, #ff453a 82%, var(--text)); }
  .etext { overflow: hidden; text-overflow: ellipsis; }

  .wrow { position: relative; }
  .wrow .row { padding-right: 30px; }
  .wrow.dim .row { opacity: 0.78; }
  .lead { width: 14px; display: grid; place-items: center; flex: none; }
  .wicon { flex: none; display: grid; }
  .wtitle { flex: 1; min-width: 0; overflow: hidden; text-overflow: ellipsis; }
  .tail { flex: none; display: grid; place-items: center; font-size: 11px; }
  .tail.muted { color: var(--text-2); }
  .row:hover .tail.muted, .row:focus-visible .tail.muted { color: rgba(255, 255, 255, 0.82); }
  .wclose {
    all: unset; box-sizing: border-box; position: absolute; right: 4px; top: 50%; width: 20px; height: 20px; margin-top: -10px; border-radius: 50%;
    display: grid; place-items: center; color: #fff; opacity: 0; background: rgba(0, 0, 0, 0.18); cursor: default;
  }
  .wrow:hover .wclose, .wrow:focus-within .wclose { opacity: 1; }
  .wclose:hover { background: rgba(0, 0, 0, 0.38); }
  .wclose:focus-visible { outline: 2px solid #fff; outline-offset: -2px; }
</style>
