<script lang="ts">
  // Launchpad overlay mode: full-screen app grid with pages, search, folders, edit mode and drag & drop.
  // Props are fixed by overlay/Surface.svelte. Pure logic lives next to this file (layout, model, dnd, pager) and is unit tested.
  import { onDestroy, onMount, untrack } from 'svelte';
  import Search from '@lucide/svelte/icons/search';
  import X from '@lucide/svelte/icons/x';
  import { getBridge, getInit } from '$lib/ipc';
  import { glassShape } from '$lib/shape';
  import type { AppEntry, LaunchpadSettings, OverlayShow } from '$lib/protocol/generated';
  import {
    computeGrid, folderGeometry, clampPage, pageAfterSwipe, pageCount, rubberBand, slotPosition,
  } from './layout';
  import {
    addToFolder, createFolder, hideApp, isFolderId, materialize, moveInFolder, moveTop, previewOrder, removeFromFolder, renameFolder,
    searchApps, suggestFolderName, toItems, toSettings, unhideAll, type Layout, type TopItem,
  } from './model';
  import { decide, edgeTurn, initialDnd, resolveDrop, slotAtPoint, type DndState, type EdgeState, type HitGrid } from './dnd';
  import { feedWheel, initialWheel } from './pager';
  import Tile from './Tile.svelte';
  import FolderPanel from './FolderPanel.svelte';
  import { t, appsCount } from './strings';

  let { onclose }: { show: OverlayShow; onclose: () => void } = $props();

  const bridge = getBridge();

  // ---------------------------------------------------------------- data
  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  const grid = $derived(computeGrid(vw, vh));

  let apps = $state.raw<AppEntry[]>([]);
  let lp = $state.raw<LaunchpadSettings>(readInitial());
  function readInitial(): LaunchpadSettings {
    try { const s = getInit().settings.launchpad; return { folders: s.folders ?? [], order: s.order ?? [], hidden: s.hidden ?? [] }; } catch { return { folders: [], order: [], hidden: [] }; }
  }

  const layout = $derived<Layout>(materialize(lp, apps));
  const items = $derived<TopItem[]>(toItems(layout, apps));
  const itemById = $derived(new Map(items.map((i) => [i.id, i])));
  const appMap = $derived(new Map(apps.map((a) => [a.id, a])));

  let query = $state('');
  const searching = $derived(query.trim().length > 0);
  const results = $derived(searching ? searchApps(apps, layout.hidden, query) : []);
  let sel = $state(0);

  let editing = $state(false);
  let page = $state(0);
  let dragX = $state(0);
  let swiping = $state(false);
  let intro = $state(true);
  let openFolderId = $state<string | null>(null);
  let renameOnOpen = $state(false);

  // ---------------------------------------------------------------- drag state
  interface Drag { id: string; from: 'top' | 'folder'; folderId: string | null; state: DndState; edge: EdgeState; settling: boolean }
  let drag = $state<Drag | null>(null);
  let ghostX = $state(0);
  let ghostY = $state(0);
  let ghostScale = $state(1.06);
  let ghostOpacity = $state(1);
  let grabX = 0;
  let grabY = 0;
  let lastX = 0;
  let lastY = 0;
  let tickTimer: ReturnType<typeof setInterval> | undefined;

  const openFolder = $derived(openFolderId ? itemById.get(openFolderId) : undefined);
  const folderItem = $derived(openFolder?.kind === 'folder' && !searching ? openFolder : undefined);
  const folderIds = $derived(folderItem ? folderItem.folder.appIds : []);
  const folderOrder = $derived(drag && drag.from === 'folder' && drag.folderId === openFolderId ? previewOrder(folderIds, drag.id, drag.state.hoverIndex) : folderIds);
  const topOrder = $derived(drag && drag.from === 'top' ? previewOrder(layout.top, drag.id, drag.state.hoverIndex) : layout.top);

  /** What the grid shows, in order. */
  const shown = $derived.by<Array<{ id: string; item: TopItem }>>(() => {
    if (searching) return results.map((a) => ({ id: a.id, item: { kind: 'app', id: a.id, name: a.name, app: a } as TopItem }));
    const out: Array<{ id: string; item: TopItem }> = [];
    for (const id of topOrder) { const item = itemById.get(id); if (item) out.push({ id, item }); }
    return out;
  });
  const pages = $derived(pageCount(shown.length, grid.perPage));
  const fgeo = $derived(folderGeometry(folderIds.length, vw, vh));
  const labelW = $derived(Math.min(grid.cellW - 20, grid.iconSize + 44));
  const hitGrid = $derived<HitGrid>({ cols: grid.cols, rows: grid.rows, cellW: grid.cellW, cellH: grid.cellH, originX: grid.originX, originY: grid.originY, iconSize: grid.iconSize, contentH: grid.contentH, perPage: grid.perPage });
  const folderHit = $derived<HitGrid>({ cols: fgeo.cols, rows: fgeo.rows, cellW: fgeo.cellW, cellH: fgeo.cellH, originX: 0, originY: 0, iconSize: fgeo.iconSize, contentH: fgeo.contentH, perPage: Infinity });

  $effect(() => { const n = pages; untrack(() => { if (page > n - 1) page = clampPage(page, n); }); });
  $effect(() => { if (folderItem === undefined && openFolderId && !drag) openFolderId = null; });
  $effect(() => { if (searching) { editing = false; openFolderId = null; } });
  $effect(() => { if (!layout.hidden.length && editing && items.length === 0) editing = false; });

  // ---------------------------------------------------------------- persistence (settings.launchpad, debounced 400 ms)
  let dirty = false;
  let saveTimer: ReturnType<typeof setTimeout> | undefined;

  function commit(next: Layout) {
    lp = toSettings(next);
    dirty = true;
    clearTimeout(saveTimer);
    saveTimer = setTimeout(flush, 400);
  }
  function flush() {
    clearTimeout(saveTimer);
    saveTimer = undefined;
    if (!dirty) return;
    dirty = false;
    const launchpad = $state.snapshot(lp) as LaunchpadSettings;
    bridge.call('settings.patch', { patch: { launchpad } }).catch((e) => console.warn('[launchpad] saving settings failed', e));
  }

  // ---------------------------------------------------------------- host wiring
  onMount(() => {
    const offApps = bridge.on('apps.changed', (l) => { apps = l.apps; });
    const offSettings = bridge.on('settings.changed', (s) => {
      if (dirty || drag || saveTimer) return; // our own edit is in flight: it will echo back
      const next = { folders: s.launchpad.folders ?? [], order: s.launchpad.order ?? [], hidden: s.launchpad.hidden ?? [] };
      if (JSON.stringify(next) !== JSON.stringify($state.snapshot(lp))) lp = next;
    });
    bridge.call('apps.list').then((l) => { if (!apps.length) apps = l.apps; }).catch(() => {});
    searchInput?.focus({ preventScroll: true });
    const timers = [40, 120, 260].map((ms) => setTimeout(() => searchInput?.focus({ preventScroll: true }), ms));
    const introTimer = setTimeout(() => (intro = false), 1000);
    return () => { offApps(); offSettings(); timers.forEach(clearTimeout); clearTimeout(introTimer); };
  });
  onDestroy(() => {
    flush();
    clearTimeout(longPressTimer);
    clearInterval(tickTimer);
    removeWindowListeners();
  });

  function launch(app: AppEntry) {
    bridge.call('apps.launch', { appId: app.id }).catch((e) => console.warn('[launchpad] launch failed', e));
    onclose();
  }

  // ---------------------------------------------------------------- helpers
  let searchInput = $state<HTMLInputElement>();
  let panelEl = $state<HTMLElement>();
  let folderGridEl = $state<HTMLElement>();
  const jiggle = (id: string) => { let h = 0; for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0; return h % 220; };
  const defaultFolderName = () => t('lp.folder');

  function slotXY(i: number) {
    const p = slotPosition(i, grid, vw);
    return p;
  }
  function introDelay(i: number): number {
    if (!intro) return 0;
    const p = slotXY(i);
    if (p.page !== 0) return 0;
    const dx = p.x + grid.cellW / 2 - vw / 2;
    const dy = p.y + grid.cellH / 2 - vh / 2;
    return Math.round(Math.min(Math.hypot(dx, dy) * 0.2, 240));
  }

  function setPage(p: number) { page = clampPage(p, pages); }

  // ---------------------------------------------------------------- pointer sessions (tiles + background swipes)
  type PS = {
    pointerId: number; startX: number; startY: number; kind: 'tile' | 'bg'; id?: string; from?: 'top' | 'folder';
    slotRect?: DOMRect; mode: 'pending' | 'swipe' | 'drag' | 'dead'; picked: boolean; startPage: number; samples: Array<{ x: number; t: number }>; onBg: boolean;
  };
  let ps: PS | null = null;
  let longPressTimer: ReturnType<typeof setTimeout> | undefined;
  let suppressClick = false;

  function addWindowListeners() {
    window.addEventListener('pointermove', onPointerMove);
    window.addEventListener('pointerup', onPointerUp);
    window.addEventListener('pointercancel', onPointerCancel);
  }
  function removeWindowListeners() {
    window.removeEventListener('pointermove', onPointerMove);
    window.removeEventListener('pointerup', onPointerUp);
    window.removeEventListener('pointercancel', onPointerCancel);
  }

  function tileDown(e: PointerEvent, id: string, from: 'top' | 'folder') {
    if (e.button !== 0 || ps) return;
    suppressClick = false;
    const slot = (e.currentTarget as HTMLElement).closest('.slot') as HTMLElement | null;
    ps = {
      pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, kind: 'tile', id, from, slotRect: slot?.getBoundingClientRect(),
      mode: 'pending', picked: editing && !searching, startPage: page, samples: [{ x: e.clientX, t: e.timeStamp }], onBg: false,
    };
    lastX = e.clientX; lastY = e.clientY;
    // keep receiving moves/up even when the pointer leaves the page (multi-monitor); click is still suppressed for drags below
    try { (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId); } catch { /* synthetic events / already released */ }
    if (!editing && !searching) {
      clearTimeout(longPressTimer);
      longPressTimer = setTimeout(() => {
        // press & hold: enter edit mode and let the same gesture pick the tile up
        if (ps && ps.mode === 'pending' && ps.kind === 'tile') { editing = true; ps.picked = true; suppressClick = true; }
      }, 520);
    }
    addWindowListeners();
  }

  function bgDown(e: PointerEvent) {
    if (e.button !== 0 || ps) return;
    const target = e.target as HTMLElement;
    if (target.closest('.tile, .x, .search, .dots, .editbar, .panel')) return;
    suppressClick = false;
    ps = { pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, kind: 'bg', mode: 'pending', picked: false, startPage: page, samples: [{ x: e.clientX, t: e.timeStamp }], onBg: true };
    lastX = e.clientX; lastY = e.clientY;
    addWindowListeners();
  }

  /** While a folder is open any press outside its panel only closes the folder. */
  function rootDownCapture(e: PointerEvent) {
    if (!folderItem || drag) return;
    const target = e.target as HTMLElement;
    if (panelEl && panelEl.contains(target)) return;
    e.stopPropagation();
    e.preventDefault();
    closeFolder();
  }

  function onPointerMove(e: PointerEvent) {
    if (!ps || e.pointerId !== ps.pointerId) return;
    // the button was released outside the page and we never saw pointerup: finish the gesture now
    if (e.pointerType === 'mouse' && e.buttons === 0) { onPointerUp(e); return; }
    lastX = e.clientX; lastY = e.clientY;
    const dx = e.clientX - ps.startX;
    const dy = e.clientY - ps.startY;
    const dist = Math.hypot(dx, dy);
    ps.samples.push({ x: e.clientX, t: e.timeStamp });
    while (ps.samples.length > 2 && e.timeStamp - ps.samples[0].t > 120) ps.samples.shift();

    if (ps.mode === 'pending') {
      if (ps.kind === 'tile' && ps.picked && dist > 4) { startDrag(e); }
      else if (dist > 8) {
        clearTimeout(longPressTimer);
        const canSwipe = (ps.kind === 'bg' || ps.from === 'top') && !folderItem && pages > 1 && Math.abs(dx) >= Math.abs(dy) * 0.6;
        if (canSwipe) { ps.mode = 'swipe'; swiping = true; suppressClick = true; }
        else { ps.mode = 'dead'; suppressClick = ps.kind === 'tile'; }
      }
    }
    if (ps.mode === 'swipe') dragX = rubberBand(dx, ps.startPage, pages);
    else if (ps.mode === 'drag') { ghostX = e.clientX - grabX; ghostY = e.clientY - grabY; tick(); }
  }

  function onPointerUp(e: PointerEvent) {
    if (!ps || e.pointerId !== ps.pointerId) return;
    const s = ps;
    clearTimeout(longPressTimer);
    if (s.mode === 'drag') finishDrag();
    else if (s.mode === 'swipe') {
      const first = s.samples[0];
      const last = s.samples[s.samples.length - 1];
      const velocity = last.t > first.t ? (last.x - first.x) / (last.t - first.t) : 0;
      page = pageAfterSwipe(s.startPage, e.clientX - s.startX, velocity, vw, pages);
      dragX = 0;
      swiping = false;
    } else if (s.mode === 'pending' && s.kind === 'bg' && s.onBg) {
      backdropClick();
    }
    ps = null;
    removeWindowListeners();
    setTimeout(() => { suppressClick = false; }, 0); // the click that follows this pointerup has been dispatched by then
  }

  function onPointerCancel(e: PointerEvent) {
    if (!ps || e.pointerId !== ps.pointerId) return;
    clearTimeout(longPressTimer);
    if (ps.mode === 'drag') abortDrag();
    if (ps.mode === 'swipe') { dragX = 0; swiping = false; }
    ps = null;
    removeWindowListeners();
  }

  /** Window lost focus mid-gesture (Alt+Tab, another monitor): never leave a half-finished drag behind. */
  function cancelGestures() {
    altArmed = false;
    if (!ps && !drag) return;
    clearTimeout(longPressTimer);
    if (drag) abortDrag();
    dragX = 0; swiping = false;
    ps = null;
    removeWindowListeners();
  }

  function backdropClick() {
    if (editing) editing = false;
    else onclose();
  }

  // ---------------------------------------------------------------- dragging tiles
  function startDrag(e: PointerEvent) {
    if (!ps || !ps.id || !ps.from) return;
    const from = ps.from;
    const order = from === 'top' ? layout.top : folderIds;
    const at = order.indexOf(ps.id);
    if (at < 0) { ps.mode = 'dead'; return; }
    clearTimeout(longPressTimer);
    ps.mode = 'drag';
    suppressClick = true;
    editing = true;
    const r = ps.slotRect;
    grabX = r ? ps.startX - r.left : grid.cellW / 2;
    grabY = r ? ps.startY - r.top : grid.cellH / 2;
    ghostX = e.clientX - grabX; ghostY = e.clientY - grabY;
    ghostScale = 1.06; ghostOpacity = 1;
    drag = { id: ps.id, from, folderId: from === 'folder' ? openFolderId : null, state: initialDnd(at), edge: { edge: 0, since: 0 }, settling: false };
    clearInterval(tickTimer);
    tickTimer = setInterval(tick, 50);
  }

  function tick() {
    const d = drag;
    if (!d || d.settling) return;
    const now = performance.now();
    if (d.from === 'folder') {
      const pr = panelEl?.getBoundingClientRect();
      const gr = folderGridEl?.getBoundingClientRect();
      if (!pr || !gr) return;
      const m = 22;
      if (lastX < pr.left - m || lastX > pr.right + m || lastY < pr.top - m || lastY > pr.bottom + m) { pullOutOfFolder(); return; }
      const slot = slotAtPoint(lastX - gr.left, lastY - gr.top, folderHit, 0, folderOrder.length);
      const next = decide(d.state, slot, { order: folderOrder, dragId: d.id, mergeable: () => false }, now);
      if (next !== d.state) d.state = next;
      return;
    }
    const slot = slotAtPoint(lastX, lastY, hitGrid, page, topOrder.length);
    const next = decide(d.state, slot, {
      order: topOrder, dragId: d.id,
      mergeable: (target) => !isFolderId(d.id) && (isFolderId(target) || itemById.get(target)?.kind === 'app'),
    }, now);
    if (next !== d.state) d.state = next;
    const edge = edgeTurn(d.edge, lastX, vw, page, pages, now);
    d.edge = edge.state;
    if (edge.turn) page = clampPage(page + edge.turn, pages);
  }

  /** The pointer left the folder panel while dragging one of its apps: it becomes a top-level item and the folder closes. */
  function pullOutOfFolder() {
    const d = drag;
    if (!d) return;
    const next = removeFromFolder(layout, d.id);
    commit(next);
    openFolderId = null;
    d.from = 'top';
    d.folderId = null;
    d.state = initialDnd(next.top.indexOf(d.id));
    d.edge = { edge: 0, since: 0 };
  }

  function finishDrag() {
    const d = drag;
    clearInterval(tickTimer);
    if (!d) return;
    tick();
    if (d.from === 'top') d.state = resolveDrop(d.state, slotAtPoint(lastX, lastY, hitGrid, page, topOrder.length), { order: topOrder, dragId: d.id });
    else {
      const gr = folderGridEl?.getBoundingClientRect();
      if (gr) d.state = resolveDrop(d.state, slotAtPoint(lastX - gr.left, lastY - gr.top, folderHit, 0, folderOrder.length), { order: folderOrder, dragId: d.id });
    }
    let settleTo: { x: number; y: number; scale: number; opacity: number } | null = null;
    if (d.from === 'top') {
      const target = d.state.mergeTarget;
      if (target) {
        const ti = topOrder.indexOf(target);
        const p = slotXY(ti);
        const cx = p.x - page * vw + grid.cellW / 2;
        const cy = p.y + grid.cellH / 2;
        settleTo = { x: cx - grabX, y: cy - grabY, scale: 0.4, opacity: 0 };
        if (isFolderId(target)) {
          commit(addToFolder(layout, d.id, target));
        } else {
          const name = suggestFolderName(appMap.get(target), appMap.get(d.id), defaultFolderName());
          const r = createFolder(layout, d.id, target, name);
          if (r) { commit(r.layout); openFolderId = r.folderId; renameOnOpen = true; }
        }
      } else {
        commit(moveTop(layout, d.id, d.state.hoverIndex));
        const p = slotXY(d.state.hoverIndex);
        settleTo = { x: p.x - page * vw, y: p.y, scale: 1, opacity: 1 };
      }
    } else if (d.folderId) {
      commit(moveInFolder(layout, d.folderId, d.id, d.state.hoverIndex));
      const gr = folderGridEl?.getBoundingClientRect();
      if (gr) {
        const i = d.state.hoverIndex;
        settleTo = { x: gr.left + (i % fgeo.cols) * fgeo.cellW, y: gr.top + Math.floor(i / fgeo.cols) * fgeo.cellH, scale: 1, opacity: 1 };
      }
    }
    d.settling = true;
    if (settleTo) { ghostX = settleTo.x; ghostY = settleTo.y; ghostScale = settleTo.scale; ghostOpacity = settleTo.opacity; }
    else { ghostOpacity = 0; }
    setTimeout(() => { if (drag === d) drag = null; }, 220);
  }

  function abortDrag() {
    clearInterval(tickTimer);
    drag = null;
  }

  // ---------------------------------------------------------------- actions
  function activate(item: TopItem) {
    if (suppressClick) { suppressClick = false; return; }
    if (item.kind === 'folder') { openFolderId = item.id; renameOnOpen = false; return; }
    if (editing) return;
    launch(item.app);
  }
  function activateInFolder(app: AppEntry) {
    if (suppressClick) { suppressClick = false; return; }
    if (editing) return;
    launch(app);
  }
  function hide(app: AppEntry) { commit(hideApp(layout, app.id)); }
  function closeFolder() { openFolderId = null; renameOnOpen = false; searchInput?.focus({ preventScroll: true }); }
  function rename(name: string) { if (openFolderId) commit(renameFolder(layout, openFolderId, name)); }
  function restoreHidden() { commit(unhideAll(layout, apps)); }

  // ---------------------------------------------------------------- keyboard + wheel
  function onKeyDown(e: KeyboardEvent) {
    if (e.isComposing) return;
    if (e.key === 'Alt') {
      e.preventDefault();
      if (!e.repeat) altArmed = !e.ctrlKey && !e.shiftKey && !e.metaKey;
      return;
    }
    altArmed = false; // Alt+<key> is a chord (Alt+Tab, Alt+F4...), not a tap
    // type-to-search: a printable key anywhere (focus may sit on a dot or a pill) goes to the search field
    if (e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey && !folderItem && document.activeElement !== searchInput && !(document.activeElement instanceof HTMLInputElement)) {
      searchInput?.focus({ preventScroll: true });
    }
    if (e.key === 'Escape') {
      e.preventDefault();
      if (drag) { abortDrag(); ps = null; removeWindowListeners(); }
      else if (folderItem) closeFolder();
      else if (editing) editing = false;
      else if (query) { query = ''; sel = 0; }
      else onclose();
      return;
    }
    if (e.key === 'Enter') {
      const inField = document.activeElement === searchInput || document.activeElement === document.body;
      if (searching && results[sel] && inField) { e.preventDefault(); launch(results[sel]); }
      return;
    }
    if (folderItem) return;
    const last = shown.length - 1;
    if (searching) {
      const cols = grid.cols;
      const move = e.key === 'ArrowRight' ? 1 : e.key === 'ArrowLeft' ? -1 : e.key === 'ArrowDown' ? cols : e.key === 'ArrowUp' ? -cols : 0;
      if (move) {
        e.preventDefault();
        sel = Math.min(Math.max(sel + move, 0), Math.max(last, 0));
        page = Math.floor(sel / grid.perPage);
      }
      return;
    }
    if (e.key === 'ArrowRight' || e.key === 'PageDown') { e.preventDefault(); setPage(page + 1); }
    else if (e.key === 'ArrowLeft' || e.key === 'PageUp') { e.preventDefault(); setPage(page - 1); }
  }
  /** Alt pressed and released alone toggles edit mode (long-press on a tile does the same). */
  let altArmed = false;
  function onKeyUp(e: KeyboardEvent) {
    if (e.key !== 'Alt') return;
    e.preventDefault();
    const tap = altArmed;
    altArmed = false;
    if (tap && !searching && items.length) editing = !editing;
  }

  let wheel = initialWheel();
  function onWheel(e: WheelEvent) {
    if (folderItem || drag) return;
    e.preventDefault();
    const scale = e.deltaMode === 1 ? 16 : e.deltaMode === 2 ? vh : 1;
    const r = feedWheel(wheel, e.deltaX * scale, e.deltaY * scale, e.timeStamp);
    wheel = r.state;
    if (r.turn) setPage(page + r.turn);
  }

  function onInput(e: Event & { currentTarget: HTMLInputElement }) {
    query = e.currentTarget.value;
    sel = 0;
    page = 0;
  }
  function refocus(e: PointerEvent) {
    if (!(e.target as HTMLElement).closest('input, button')) e.preventDefault();
  }

  // folder panel transform origin: the tile it grows out of
  const folderOrigin = $derived.by(() => {
    if (!folderItem) return { x: 0, y: 0 };
    const i = topOrder.indexOf(folderItem.id);
    const p = slotXY(Math.max(i, 0));
    const cx = p.x - page * vw + grid.cellW / 2;
    const cy = p.y + grid.cellH / 2;
    const w = fgeo.width + 56;
    const h = 26 + 40 + 8 + fgeo.height + 24;
    return { x: cx - (vw / 2 - w / 2), y: cy - (vh / 2 - h * 0.52) };
  });
  const editHint = $derived(editing && !searching);
</script>

<svelte:window bind:innerWidth={vw} bind:innerHeight={vh} onkeydown={onKeyDown} onkeyup={onKeyUp} onfocus={() => searchInput?.focus({ preventScroll: true })} onblur={cancelGestures} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="lp" onpointerdowncapture={rootDownCapture} onpointerdown={bgDown} onwheel={onWheel}>
  <div class="backdrop" use:glassShape={{ radius: 0 }}></div>

  <div class="search glass" use:glassShape={{ radius: 11 }} onpointerdown={refocus}>
    <Search size={15} strokeWidth={2} />
    <input
      bind:this={searchInput}
      type="text"
      placeholder={t('lp.search')}
      aria-label={t('lp.searchLabel')}
      autocomplete="off"
      spellcheck="false"
      value={query}
      oninput={onInput}
    />
    {#if query}
      <button type="button" class="clear" aria-label={t('lp.clear')} onclick={() => { query = ''; sel = 0; searchInput?.focus(); }}><X size={12} strokeWidth={2.6} /></button>
    {/if}
  </div>

  <div class="viewport" class:dim={!!folderItem} role="presentation" inert={!!folderItem}>
    <div class="track" class:swiping style:transform="translate3d({-page * vw + dragX}px, 0, 0)" role="list" aria-label={t('lp.dialog')}>
      {#each shown as s, i (s.id)}
        {@const pos = slotXY(i)}
        {@const onPage = pos.page === page}
        <div
          class="slot"
          class:ghosted={drag?.id === s.id}
          role="listitem"
          style:width="{grid.cellW}px"
          style:height="{grid.cellH}px"
          style:transform="translate3d({pos.x}px, {pos.y}px, 0)"
          style:--jd="{-jiggle(s.id)}ms"
          style:--d="{introDelay(i)}ms"
          style:--ix="{(grid.cellW - grid.iconSize) / 2 - 9}px"
          style:--iy="{(grid.cellH - grid.contentH) / 2 - 9}px"
        >
          <button
            type="button"
            class="tile"
            class:intro
            tabindex={onPage ? 0 : -1}
            aria-label={s.item.kind === 'folder' ? t('lp.openFolder', { name: s.item.name || t('lp.folder'), count: appsCount(s.item.apps.length) }) : s.item.name}
            onpointerdown={(e) => !searching && tileDown(e, s.id, 'top')}
            onclick={() => activate(s.item)}
            onfocus={() => { if (!onPage) page = pos.page; }}
          >
            <Tile
              name={s.item.kind === 'folder' ? s.item.name || t('lp.folder') : s.item.name}
              app={s.item.kind === 'app' ? s.item.app : undefined}
              folderApps={s.item.kind === 'folder' ? s.item.apps : undefined}
              size={grid.iconSize}
              labelWidth={labelW}
              editing={editing && !searching}
              merge={drag?.state.mergeTarget === s.id}
              selected={searching && i === sel}
            />
          </button>
          {#if editing && !searching && s.item.kind === 'app'}
            <button type="button" class="x" aria-label={t('lp.remove', { name: s.item.name })} onclick={() => s.item.kind === 'app' && hide(s.item.app)}><X size={13} strokeWidth={3} /></button>
          {/if}
        </div>
      {/each}
    </div>

    {#if shown.length === 0}
      <div class="empty" role="status">
        <div class="e-title">{searching ? t('lp.empty') : t('lp.noApps')}</div>
        <div class="e-hint">{searching ? t('lp.empty.hint') : t('lp.noApps.hint')}</div>
      </div>
    {/if}
  </div>

  {#if pages > 1}
    <div class="dots" role="group" aria-label={t('lp.pages')} use:glassShape={{ radius: 12 }}>
      {#each Array(pages) as _, p (p)}
        <button type="button" class="dot" class:on={p === page} aria-label={t('lp.page', { n: p + 1, total: pages })} aria-current={p === page ? 'page' : undefined} onclick={() => setPage(p)}></button>
      {/each}
    </div>
  {/if}

  {#if editHint}
    <div class="editbar glass" use:glassShape={{ radius: 16 }}>
      <span class="hint">{t('lp.editHint')}</span>
      {#if layout.hidden.length}<button type="button" class="pill" onclick={restoreHidden}>{t('lp.restore', { n: layout.hidden.length })}</button>{/if}
      <button type="button" class="pill primary" onclick={() => (editing = false)}>{t('lp.done')}</button>
    </div>
  {/if}

  {#if folderItem}
    <FolderPanel
      folder={folderItem.folder}
      apps={appMap}
      order={folderOrder}
      geometry={fgeo}
      {editing}
      dragId={drag?.from === 'folder' ? drag.id : null}
      autoRename={renameOnOpen}
      originX={folderOrigin.x}
      originY={folderOrigin.y}
      {jiggle}
      onrename={rename}
      onclose={closeFolder}
      ontiledown={(e, id) => tileDown(e, id, 'folder')}
      onactivate={activateInFolder}
      onhide={hide}
      bind:gridEl={folderGridEl}
      bind:panelEl
    />
  {/if}

  {#if drag}
    {@const item = itemById.get(drag.id)}
    {@const gApp = appMap.get(drag.id)}
    <div
      class="ghost"
      class:settling={drag.settling}
      class:in-folder={drag.from === 'folder'}
      style:width="{drag.from === 'folder' ? fgeo.cellW : grid.cellW}px"
      style:height="{drag.from === 'folder' ? fgeo.cellH : grid.cellH}px"
      style:transform="translate3d({ghostX}px, {ghostY}px, 0) scale({ghostScale})"
      style:opacity={ghostOpacity}
      aria-hidden="true"
    >
      {#if item}
        <Tile name={item.name || t('lp.folder')} app={item.kind === 'app' ? item.app : undefined} folderApps={item.kind === 'folder' ? item.apps : undefined} size={grid.iconSize} labelWidth={labelW} editing={false} />
      {:else if gApp}
        <Tile name={gApp.name} app={gApp} size={drag.from === 'folder' ? fgeo.iconSize : grid.iconSize} labelWidth={(drag.from === 'folder' ? fgeo.cellW : grid.cellW) - 12} editing={false} />
      {/if}
    </div>
  {/if}
</div>

<style>
  .lp { position: fixed; inset: 0; touch-action: none; overflow: hidden; }

  .backdrop {
    position: absolute; inset: 0;
    --lp-shade: rgba(0, 0, 0, 0.14);
    background: linear-gradient(var(--lp-shade), var(--lp-shade)), var(--backdrop-dim);
    backdrop-filter: blur(34px) saturate(150%); -webkit-backdrop-filter: blur(34px) saturate(150%);
    animation: fade-in 220ms var(--ease-out) both;
  }
  :global(:root[data-theme='light']) .backdrop { --lp-shade: rgba(0, 0, 0, 0.26); }
  :global(:root[data-reduce-transparency='true']) .backdrop { backdrop-filter: none; -webkit-backdrop-filter: none; background: var(--glass-bg-strong); }
  @keyframes fade-in { from { opacity: 0; } to { opacity: 1; } }
  :global(:root[data-reduce-transparency='true']) .search { background: rgba(58, 58, 66, 0.94); }
  :global(:root[data-reduce-transparency='true']) .editbar { background: rgba(40, 40, 46, 0.96); }

  .search {
    position: absolute; top: 40px; left: 50%; translate: -50% 0; width: 248px; height: 34px; z-index: 3;
    display: flex; align-items: center; gap: 8px; padding: 0 10px; border-radius: 11px; color: rgba(255, 255, 255, 0.85);
    background: rgba(255, 255, 255, 0.16); border: 0.5px solid rgba(255, 255, 255, 0.24);
    box-shadow: 0 1px 0 rgba(255, 255, 255, 0.12) inset, 0 4px 14px rgba(0, 0, 0, 0.12);
    animation: search-in 260ms var(--ease-out) both;
  }
  @keyframes search-in { from { opacity: 0; transform: translateY(-8px); } to { opacity: 1; transform: none; } }
  .search:focus-within { background: rgba(255, 255, 255, 0.22); box-shadow: 0 0 0 2px rgba(255, 255, 255, 0.45), 0 4px 14px rgba(0, 0, 0, 0.12); }
  .search input { flex: 1; min-width: 0; height: 100%; border: 0; outline: 0; background: transparent; color: #fff; font: 400 14px/1 var(--font-ui); user-select: text; caret-color: #fff; }
  .search input::placeholder { color: rgba(255, 255, 255, 0.62); }
  .clear { display: grid; place-items: center; width: 16px; height: 16px; padding: 0; border: 0; border-radius: 50%; background: rgba(255, 255, 255, 0.5); color: rgba(0, 0, 0, 0.7); }
  .clear:hover { background: rgba(255, 255, 255, 0.7); }
  .clear:focus-visible { outline: 2px solid #fff; outline-offset: 1px; }

  .viewport { position: absolute; inset: 0; transition: opacity var(--dur) var(--ease-out), filter var(--dur) var(--ease-out); }
  .viewport.dim { opacity: 0.5; filter: blur(8px); }
  .track { position: absolute; left: 0; top: 0; width: 100%; height: 100%; will-change: transform; transition: transform 420ms var(--ease-out); }
  .track.swiping { transition: none; }
  .slot { position: absolute; left: 0; top: 0; display: grid; place-items: center; transition: transform 300ms var(--ease-out); }
  .slot.ghosted { visibility: hidden; }

  .tile { all: unset; box-sizing: border-box; display: grid; place-items: center; padding: 6px; cursor: default; border-radius: 22px; touch-action: none; }
  .tile:focus-visible { outline: 3px solid var(--accent); outline-offset: -8px; }
  .tile :global(.icon) { transition: transform 140ms var(--ease-out), filter 140ms; }
  .tile:active :global(.icon) { transform: scale(0.94); filter: drop-shadow(0 5px 9px rgba(0, 0, 0, 0.3)) brightness(0.85); }
  .tile.intro { animation: tile-in 380ms var(--ease-out) both; animation-delay: var(--d, 0ms); }
  @keyframes tile-in { from { opacity: 0; transform: scale(1.1); } to { opacity: 1; transform: none; } }
  :global(:root[data-reduce-motion='true']) .tile.intro { animation: none; }

  .x {
    position: absolute; left: var(--ix); top: var(--iy); width: 22px; height: 22px; padding: 0; display: grid; place-items: center; z-index: 2;
    border-radius: 50%; border: 0.5px solid rgba(255, 255, 255, 0.4); background: rgba(58, 58, 64, 0.92); color: #fff;
    box-shadow: 0 1px 4px rgba(0, 0, 0, 0.45); animation: pop 170ms var(--ease-spring) both;
  }
  .x:hover { background: rgba(88, 88, 96, 0.96); }
  .x:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }
  @keyframes pop { from { transform: scale(0.4); opacity: 0; } to { transform: none; opacity: 1; } }

  .dots { position: absolute; left: 50%; bottom: 20px; translate: -50% 0; display: flex; gap: 4px; padding: 5px 9px; border-radius: 12px; z-index: 3; background: rgba(0, 0, 0, 0.14); }
  .dot { all: unset; box-sizing: border-box; width: 18px; height: 18px; display: grid; place-items: center; cursor: default; border-radius: 50%; }
  .dot::before { content: ''; width: 8px; height: 8px; border-radius: 50%; background: rgba(255, 255, 255, 0.4); transition: background 160ms, transform 160ms var(--ease-spring); }
  .dot:hover::before { background: rgba(255, 255, 255, 0.65); }
  .dot.on::before { background: #fff; transform: scale(1.1); }
  .dot:focus-visible { outline: 2px solid #fff; outline-offset: 0; }

  .editbar {
    position: absolute; left: 50%; bottom: 54px; translate: -50% 0; z-index: 3; display: flex; align-items: center; gap: 10px; padding: 6px 8px 6px 16px; border-radius: 16px;
    background: rgba(30, 30, 36, 0.5); color: #fff; border: 0.5px solid rgba(255, 255, 255, 0.2); animation: bar-in 220ms var(--ease-out) both;
    max-width: calc(100vw - 24px);
  }
  @keyframes bar-in { from { opacity: 0; transform: translateY(8px); } to { opacity: 1; transform: none; } }
  .editbar .hint { font-size: 12px; color: rgba(255, 255, 255, 0.78); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
  .pill { height: 26px; padding: 0 12px; border: 0; border-radius: 13px; background: rgba(255, 255, 255, 0.18); color: #fff; font: 500 12px/1 var(--font-ui); white-space: nowrap; }
  .pill:hover { background: rgba(255, 255, 255, 0.28); }
  .pill.primary { background: var(--accent); }
  .pill.primary:hover { background: color-mix(in oklab, var(--accent) 85%, white); }
  .pill:focus-visible { outline: 2px solid #fff; outline-offset: 2px; }

  .empty { position: absolute; left: 0; right: 0; top: 40%; text-align: center; color: #fff; text-shadow: 0 1px 3px rgba(0, 0, 0, 0.5); }
  .e-title { font-size: 18px; font-weight: 600; }
  .e-hint { margin-top: 4px; font-size: 13px; color: rgba(255, 255, 255, 0.75); }

  .ghost { position: fixed; left: 0; top: 0; z-index: 60; pointer-events: none; display: grid; place-items: center; transition: none; will-change: transform; }
  .ghost.settling { transition: transform 200ms var(--ease-out), opacity 200ms; }
  .ghost :global(.icon) { filter: drop-shadow(0 12px 18px rgba(0, 0, 0, 0.4)); }
  .ghost :global(.label) { color: #fff; }
  .ghost.in-folder :global(.label) { color: var(--text); text-shadow: none; }
</style>
