<script lang="ts">
  // The desktop layer of one monitor: wallpaper, icon grid and widgets. It never steals focus from applications (the host shows the
  // window without activating it); everything here is driven by desktop.state.
  import { getBridge } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import { defaultDesktopState, type WidgetInstance, type WidgetSize } from '$lib/protocol/generated';
  import Wallpaper from './Wallpaper.svelte';
  import IconLayer from './IconLayer.svelte';
  import WidgetFrame from './WidgetFrame.svelte';
  import ContextMenu from './ContextMenu.svelte';
  import type { MenuItem } from './menu';
  import { gridMetrics, surfaceInsets, type Point } from './layout';
  import { applyOverrides, dropOverride, pruneOverrides, type Overrides } from './optimistic';
  import { widgetMenu, type MenuAction } from './widgetMenu';
  import { liveSettings, callQuiet } from './shared.svelte';
  import { t } from './strings';

  const bridge = getBridge();
  const desktop = hostState('desktop.state', defaultDesktopState());
  const settings = liveSettings();

  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  const viewport = $derived({ width: vw, height: vh });
  const insets = $derived(surfaceInsets(settings.value));
  const ready = $derived(desktop.value.monitorId !== '');
  const metrics = $derived(gridMetrics(desktop.value.iconSize || 64, viewport, insets));

  // ---- widgets: host state + optimistic overrides (move / size / config) until desktop.state catches up
  let overrides = $state<Overrides<WidgetInstance>>({});
  let removing = $state<Set<string>>(new Set());
  const widgets = $derived(applyOverrides(desktop.value.widgets, overrides).filter((w) => !removing.has(w.id)));
  $effect(() => {
    const items = desktop.value.widgets;
    const pruned = pruneOverrides(overrides, items);
    if (Object.keys(pruned).length !== Object.keys(overrides).length || JSON.stringify(pruned) !== JSON.stringify(overrides)) overrides = pruned;
  });

  /** Show `patch` immediately, run the host call, and forget the override on failure (or shortly after success, when the host state has arrived). */
  function optimistic(id: string, patch: Partial<WidgetInstance>, call: () => Promise<unknown>) {
    overrides = { ...overrides, [id]: { ...overrides[id], ...patch } };
    const keys = Object.keys(patch) as Array<keyof WidgetInstance>;
    call().then(
      () => setTimeout(() => { overrides = dropOverride(overrides, id, keys); }, 800),
      (e) => { overrides = dropOverride(overrides, id, keys); report('widget update', e); },
    );
  }
  const report = (what: string, e: unknown) => bridge.emit('surface.log', { level: 'warn', message: `${what} failed: ${e instanceof Error ? e.message : String(e)}` });

  const moveWidget = (w: WidgetInstance, p: Point) => optimistic(w.id, { x: p.x, y: p.y }, () => bridge.call('desktop.moveWidget', { id: w.id, x: p.x, y: p.y }));
  const saveConfig = (w: WidgetInstance, config: Record<string, unknown>) => optimistic(w.id, { config }, () => bridge.call('desktop.updateWidget', { id: w.id, config }));
  const resizeWidget = (w: WidgetInstance, size: WidgetSize) => { raisedId = w.id; optimistic(w.id, { size }, () => bridge.call('desktop.updateWidget', { id: w.id, size })); };
  let raisedId = $state<string | null>(null);
  function removeWidget(w: WidgetInstance) {
    removing = new Set([...removing, w.id]);
    bridge.call('desktop.removeWidget', { id: w.id }).catch((e) => report('removeWidget', e)).finally(() => {
      setTimeout(() => { removing = new Set([...removing].filter((x) => x !== w.id)); }, 800);
    });
  }

  // ---- icons
  const openIcons = (ids: string[]) => { for (const id of ids) callQuiet(bridge.call('desktop.open', { id }), 'desktop.open'); };
  const moveIcons = async (moves: Array<{ id: string; x: number; y: number }>) => {
    await Promise.all(moves.map((m) => bridge.call('desktop.moveIcon', m)));
  };

  // ---- menus
  let menu = $state<{ x: number; y: number; label: string; items: MenuItem[]; run: (id: string) => void } | null>(null);

  function iconMenu(id: string, at: Point) {
    menu = { x: at.x, y: at.y, label: t('desk.icons'), items: [{ id: 'open', label: t('icon.open') }], run: () => openIcons([id]) };
  }

  function runWidgetAction(w: WidgetInstance, action: MenuAction) {
    if (action.kind === 'size') resizeWidget(w, action.size);
    else if (action.kind === 'remove') removeWidget(w);
    else if (action.kind === 'config') saveConfig(w, action.config);
    else callQuiet(bridge.call('settings.openWindow', { page: action.page }), 'settings.openWindow');
  }

  function widgetMenuAt(w: WidgetInstance, at: Point) {
    const m = widgetMenu(w, t);
    menu = { x: at.x, y: at.y, label: t('widget.menu'), items: m.items, run: (id) => { const a = m.resolve(id); if (a) runWidgetAction(w, a); } };
  }

  function choose(id: string) {
    const m = menu;
    menu = null;
    m?.run(id);
  }

  function desktopMenu(at: Point) {
    menu = null;
    callQuiet(
      bridge.call('overlay.open', { mode: 'popover', popover: { kind: 'desktopMenu', anchor: { x: at.x, y: at.y, width: 1, height: 1 } }, monitorId: desktop.value.monitorId }),
      'overlay.open desktopMenu',
    );
  }
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} onkeydown={(e) => { if (e.key === 'Escape' && menu) { menu = null; } }} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="desktop" data-monitor={desktop.value.monitorId} role="application" aria-label={t('desk.label')} oncontextmenu={(e) => e.preventDefault()}>
  {#if ready && desktop.value.enabled}
    <Wallpaper url={desktop.value.wallpaperUrl} fit={desktop.value.wallpaperFit} />
    {#if desktop.value.icons.length}
      <IconLayer
        icons={desktop.value.icons} {metrics}
        onopen={openIcons} onmove={moveIcons}
        oniconcontext={iconMenu} onbackgroundcontext={desktopMenu}
      />
    {:else}
      <!-- no icons on this monitor: the empty desktop still offers the desktop menu -->
      <div class="empty" role="presentation" oncontextmenu={(e) => { e.preventDefault(); desktopMenu({ x: e.clientX, y: e.clientY }); }}></div>
    {/if}
    <div class="widgets">
      {#each widgets as w (w.id)}
        <WidgetFrame
          widget={w} {viewport} {insets} raised={raisedId === w.id} onraise={() => (raisedId = w.id)}
          onmove={(p) => moveWidget(w, p)}
          onmenu={(at) => widgetMenuAt(w, at)}
          onconfig={(c) => saveConfig(w, c)}
        />
      {/each}
    </div>
  {/if}
  {#if menu}
    <ContextMenu x={menu.x} y={menu.y} label={menu.label} items={menu.items} onselect={choose} onclose={() => (menu = null)} />
  {/if}
</div>

<style>
  .desktop { position: fixed; inset: 0; overflow: hidden; }
  .widgets { position: absolute; inset: 0; pointer-events: none; }
  .widgets > :global(*) { pointer-events: auto; }
  .empty { position: absolute; inset: 0; }
</style>
