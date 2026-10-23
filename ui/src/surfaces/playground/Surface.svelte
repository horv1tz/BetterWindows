<script lang="ts">
  // PLAYGROUND (#/playground): every surface composed like the real desktop, in one page, on top of the shared in-browser mock host.
  // Same-origin iframes: desktop (bottom) < faux windows < menu bar < dock < overlay (top). The iframes share ONE MockHost through
  // window.top, so a popover opened in the menu bar iframe is rendered by the overlay iframe, exactly like in the real multi-WebView app.
  import { onMount } from 'svelte';
  import Moon from '@lucide/svelte/icons/moon';
  import Sun from '@lucide/svelte/icons/sun';
  import RotateCcw from '@lucide/svelte/icons/rotate-ccw';
  import SettingsIcon from '@lucide/svelte/icons/settings';
  import Monitor from '@lucide/svelte/icons/monitor';
  import MonitorUp from '@lucide/svelte/icons/monitor-up';
  import type { DockPosition, OverlayShow } from '$lib/protocol/generated';
  import { i18n } from '$lib/i18n.svelte';
  import { applyTheme } from '$lib/theme';
  import { getMockHost, type MockContext, type MockHost } from '$mock/host';
  import { appByName } from '$mock/fixtures';
  import { taps } from '$mock/domains/widgets';
  import FauxWindow from './FauxWindow.svelte';
  import SettingsWindow from './SettingsWindow.svelte';
  import {
    MENUBAR, clampWindow, defaultWindows, dockFrame, monitorIndex, raise, screenFrames, topWindow, type FauxSpec, type Frame, type ZOrder,
  } from './layout';
  import { overlayAction, shortcutOf, type OverlayMode } from './shortcuts';
  import { t } from './strings';

  // ---- take over ?theme= before the host exists: the mock host would otherwise force it forever and ignore every later toggle
  const params = new URLSearchParams(location.search);
  const initialTheme = params.get('theme');
  if (initialTheme) {
    const stripped = new URLSearchParams(params);
    stripped.delete('theme');
    history.replaceState(null, '', `${location.pathname}${stripped.size ? `?${stripped}` : ''}${location.hash}`);
  }
  const host = getMockHost() as MockHost & { showOverlay?: (req: { mode: OverlayMode; monitorId?: string }) => void; setActiveApp?: (id: string | null) => void; surfaceOffsets: Record<string, { x: number; y: number }> };
  const tap = taps(host);
  const fakeCtx = { surface: 'settings', monitorId: undefined, bridge: undefined as never } as MockContext;

  const monitorCount = params.get('monitors') === '2' ? 2 : 1;
  const showStrip = params.get('strip') !== '0';
  // faux windows: on for one monitor, off for two (the point there is to compare the two desktops) unless ?windows=1
  const showWindows = params.get('windows') === '1' || (params.get('windows') !== '0' && monitorCount === 1);
  const base = `${location.pathname}${new URLSearchParams([...params].filter(([k]) => k !== 'theme' && k !== 'monitors')).toString().replace(/^(?=.)/, '?')}`;

  let ready = $state(false);
  let mode = $state<'light' | 'dark'>('dark');
  const initialLang = params.get('lang') === 'ru' ? 'ru' : 'en';
  let lang = $state(initialLang);
  i18n.set(initialLang);

  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  const frames = $derived(screenFrames({ width: vw, height: vh }, monitorCount));
  const primary = $derived(frames[0]);

  // ---- dock: the dock UI owns its window size (surface.resize); the position follows settings.dock.position
  let dockSize = $state({ width: 520, height: 104 });
  let dockPos = $state<DockPosition>('bottom');
  const dock = $derived(dockFrame({ width: primary.width, height: primary.height }, dockSize, dockPos));

  // the frozen overlay mock translates popover anchors by host.surfaceOffsets[source surface]
  $effect(() => {
    host.surfaceOffsets.dock = { x: dock.left, y: dock.top };
    host.surfaceOffsets.menubar = { x: 0, y: 0 };
    host.surfaceOffsets.desktop = { x: 0, y: 0 };
  });

  // ---- overlay
  let overlay = $state<{ visible: boolean; mode: OverlayMode | null; monitorId: string; since: number }>({ visible: false, mode: null, monitorId: '1', since: 0 });
  const overlayFrame = $derived(frames[monitorIndex(overlay.monitorId, monitorCount)]);
  let overlayEl: HTMLIFrameElement | undefined = $state();
  let pointerMonitor = '1';

  function showOverlay(m: Exclude<OverlayMode, 'popover'>) {
    host.showOverlay?.({ mode: m, monitorId: pointerMonitor });
    setTimeout(() => overlayEl?.contentWindow?.focus(), 80);
  }
  function hideOverlay() {
    host.emit('overlay.hide', undefined as never, (c) => c.surface === 'overlay');
    overlay = { ...overlay, visible: false, mode: null };
  }

  // ---- keyboard: the real app registers global hotkeys; here the page (and every same-origin iframe) listens
  function onKey(e: KeyboardEvent) {
    const sc = shortcutOf(e);
    if (!sc) return;
    e.preventDefault();
    if (sc === 'escape') { if (overlay.visible) hideOverlay(); return; }
    if (overlayAction(overlay.visible ? overlay.mode : null, sc) === 'hide') hideOverlay(); else showOverlay(sc);
  }
  /** Svelte action for every surface iframe: forwards the playground hotkeys typed while that iframe has the focus. */
  function surfaceFrame(node: HTMLIFrameElement) {
    const attach = () => { try { node.contentWindow?.addEventListener('keydown', onKey, true); } catch { /* cross-origin: nothing to do */ } };
    node.addEventListener('load', attach);
    attach();
    return { destroy() { node.removeEventListener('load', attach); try { node.contentWindow?.removeEventListener('keydown', onKey, true); } catch { /* gone */ } } };
  }

  // ---- faux application windows (primary screen)
  let specs = $state<FauxSpec[]>([]);
  let wframes = $state<Record<string, Frame>>({});
  let z = $state<ZOrder>({});
  let closed = $state<Set<string>>(new Set());
  const activeWin = $derived(topWindow(z));
  function raiseWindow(id: string) {
    z = raise(z, id);
    const spec = specs.find((s) => s.id === id);
    if (spec) host.setActiveApp?.(appByName(spec.appName)?.id ?? null);
  }

  // ---- settings window
  let settings = $state<{ open: boolean; page?: string; frame: Frame }>({ open: false, frame: { left: 0, top: 0, width: 860, height: 600 } });
  let pendingPage: string | undefined;
  const settingsSrc = `${base}#/settings`;
  const SETTINGS_Z = 50;
  function openSettings(page?: string) {
    if (settings.open) { host.emit('settings.navigate', { page }, (c) => c.surface === 'settings'); settings = { ...settings, page }; return; }
    pendingPage = page;
    const w = Math.min(860, primary.width - 40), h = Math.min(600, primary.height - 80);
    settings = { open: true, page, frame: { left: Math.round((primary.width - w) / 2), top: Math.round(MENUBAR + (primary.height - MENUBAR - h) / 2.4), width: w, height: h } };
  }
  const closeSettings = () => { settings = { ...settings, open: false }; };

  // ---- control strip
  async function patch(p: object) { await host.dispatchCall(fakeCtx, 'settings.patch', { patch: p }); }
  const toggleTheme = () => patch({ appearance: { mode: mode === 'dark' ? 'light' : 'dark' } });
  const toggleLang = () => patch({ general: { language: lang === 'ru' ? 'en' : 'ru' } });
  function reload(extra: Record<string, string>) {
    const q = new URLSearchParams(location.search);
    q.set('theme', mode); q.set('lang', lang);
    for (const [k, v] of Object.entries(extra)) q.set(k, v);
    location.assign(`${location.pathname}?${q}${location.hash}`);
  }

  onMount(() => {
    let alive = true;
    const offs: Array<() => void> = [];
    (async () => {
      // settle the initial theme through the same call every settings change uses
      if (initialTheme === 'dark' || initialTheme === 'light') await patch({ appearance: { mode: initialTheme } });
      if (!alive) return;
      mode = host.theme.mode;
      dockPos = host.settings.dock.position;
      applyTheme(host.theme);
      specs = showWindows ? defaultWindows({ width: primary.width, height: primary.height }) : [];
      wframes = Object.fromEntries(specs.map((s) => [s.id, s.frame]));
      z = Object.fromEntries(specs.map((s, i) => [s.id, i + 1]));
      ready = true;
      const top = topWindow(z);
      if (top) host.setActiveApp?.(appByName(specs.find((s) => s.id === top)!.appName)?.id ?? null);
    })();

    offs.push(
      tap.onEmit('theme.changed', (p) => { const th = p as typeof host.theme; mode = th.mode; applyTheme(th); }),
      tap.onEmit('locale.changed', (p) => { lang = (p as { locale: string }).locale; i18n.set(lang); }),
      tap.onEmit('settings.changed', (p) => { dockPos = (p as typeof host.settings).dock.position; }),
      tap.onEmit('overlay.show', (p) => {
        const s = p as OverlayShow;
        overlay = { visible: true, mode: s.mode, monitorId: s.monitorId, since: Date.now() };
      }),
      tap.onEmit('overlay.hide', () => { overlay = { ...overlay, visible: false, mode: null }; }),
      tap.onCall('settings.openWindow', (p) => { openSettings((p as { page?: string } | undefined)?.page); }),
    );
    host.onUiEvent('surface.resize', (p, ctx) => { if (alive && ctx.surface === 'dock') dockSize = { width: Math.round(p.width), height: Math.round(p.height) }; });
    host.onSurfaceReady((ctx) => {
      if (alive && ctx.surface === 'settings' && pendingPage !== undefined) { const page = pendingPage; pendingPage = undefined; host.emitTo(ctx, 'settings.navigate', { page }); }
    });

    // safety net: if the overlay renders nothing (it closed itself without telling anyone) stop swallowing the pointer
    const poll = setInterval(() => {
      if (!overlay.visible || Date.now() - overlay.since < 700) return;
      const root = overlayEl?.contentDocument?.getElementById('app');
      if (root && !root.firstElementChild) overlay = { ...overlay, visible: false, mode: null };
    }, 400);
    return () => { alive = false; offs.forEach((f) => f()); clearInterval(poll); };
  });

  const frameSrc = (surface: string) => `${base}#/${surface}`;
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} onkeydown={onKey} />

<div class="playground" data-ready={ready}>
  {#if ready}
    {#each frames as f, i (i)}
      {@const id = String(i + 1)}
      <div class="screen" style:left="{f.left}px" style:top="{f.top}px" style:width="{f.width}px" style:height="{f.height}px" onpointerenter={() => (pointerMonitor = id)} role="presentation" data-monitor={id}>
        <iframe class="surface desktop" title="desktop {id}" src={frameSrc(`desktop/${id}`)} use:surfaceFrame></iframe>

        {#if i === 0}
          {#each specs as spec (spec.id)}
            {#if !closed.has(spec.id) && wframes[spec.id]}
              <FauxWindow
                {spec} frame={wframes[spec.id]} z={z[spec.id] ?? 1} active={activeWin === spec.id && !settings.open} bounds={{ width: f.width, height: f.height }}
                onraise={() => raiseWindow(spec.id)} onmove={(nf) => (wframes = { ...wframes, [spec.id]: nf })}
                onclose={() => (closed = new Set([...closed, spec.id]))}
              />
            {/if}
          {/each}
          {#if settings.open}
            <SettingsWindow
              src={settingsSrc} frame={settings.frame} z={SETTINGS_Z} bounds={{ width: f.width, height: f.height }}
              onmove={(nf) => (settings = { ...settings, frame: nf })} onclose={closeSettings} onraise={() => {}} onframe={() => {}}
            />
          {/if}
        {/if}

        <iframe class="surface menubar" title="menubar {id}" src={frameSrc(`menubar/${id}`)} style:height="{MENUBAR}px" use:surfaceFrame></iframe>

        {#if i === 0}
          <iframe
            class="surface dock" title="dock" src={frameSrc('dock')} use:surfaceFrame
            style:left="{dock.left}px" style:top="{dock.top}px" style:width="{dock.width}px" style:height="{dock.height}px"
          ></iframe>
        {/if}
      </div>
    {/each}

    <iframe
      class="surface overlay" class:live={overlay.visible} title="overlay" src={frameSrc('overlay')} bind:this={overlayEl} use:surfaceFrame
      style:left="{overlayFrame.left}px" style:top="{overlayFrame.top}px" style:width="{overlayFrame.width}px" style:height="{overlayFrame.height}px"
    ></iframe>

    {#if showStrip}
      <div class="strip glass" role="toolbar" aria-label={t('pg.strip')} aria-describedby="pg-shortcuts" title={t('pg.shortcuts')}>
        <span id="pg-shortcuts" class="sr-only">{t('pg.shortcuts')}</span>
        <button type="button" data-action="theme" aria-label={mode === 'dark' ? t('pg.theme.light') : t('pg.theme.dark')} title={mode === 'dark' ? t('pg.theme.light') : t('pg.theme.dark')} onclick={toggleTheme}>
          {#if mode === 'dark'}<Sun size={15} />{:else}<Moon size={15} />{/if}
        </button>
        <button type="button" data-action="lang" class="lang" aria-label={t('pg.language')} title={lang === 'ru' ? t('pg.language.en') : t('pg.language.ru')} onclick={toggleLang}>{lang === 'ru' ? 'RU' : 'EN'}</button>
        <button type="button" data-action="monitors" aria-label={t('pg.monitors')} title={monitorCount === 2 ? t('pg.monitors.one') : t('pg.monitors.two')} onclick={() => reload({ monitors: monitorCount === 2 ? '1' : '2' })}>
          {#if monitorCount === 2}<MonitorUp size={15} />{:else}<Monitor size={15} />{/if}
        </button>
        <button type="button" data-action="settings" aria-label={t('pg.settings')} title={t('pg.settings')} onclick={() => openSettings()}><SettingsIcon size={15} /></button>
        <button type="button" data-action="reset" aria-label={t('pg.reset')} title={t('pg.reset')} onclick={() => reload({})}><RotateCcw size={15} /></button>
      </div>
    {/if}
  {/if}
</div>

<style>
  :global(body) { background: #05060a; }
  .playground { position: fixed; inset: 0; overflow: hidden; }
  .screen { position: absolute; overflow: hidden; background: #000; }
  /* no color-scheme override: an iframe whose scheme differs from its document's gets an opaque canvas, which would hide everything below it */
  iframe { border: 0; background: transparent; }
  .surface { position: absolute; }
  .desktop { inset: 0; width: 100%; height: 100%; }
  .menubar { left: 0; top: 0; width: 100%; z-index: 100; }
  .dock { z-index: 110; }
  .overlay { z-index: 1000; pointer-events: none; }
  .overlay.live { pointer-events: auto; }
  .strip {
    position: fixed; right: 12px; bottom: 12px; z-index: 2000; display: flex; gap: 2px; padding: 4px; border-radius: 12px; opacity: 0.55;
    transition: opacity var(--dur) ease;
  }
  .strip:hover, .strip:focus-within { opacity: 1; }
  .strip button { height: 28px; min-width: 28px; padding: 0 6px; display: grid; place-items: center; border: 0; border-radius: 8px; background: none; color: var(--text); cursor: default; }
  .strip button:hover { background: var(--hover); }
  .strip button:active { background: var(--press); }
  .strip button:focus-visible { outline: 2px solid var(--accent); outline-offset: -2px; }
  .lang { font-size: 11px; font-weight: 700; letter-spacing: 0.03em; }
</style>
