<script lang="ts">
  // Mission Control: every window of the selected desktop in a non-overlapping grid, with a desktop strip on top.
  // Overlay panels are not reported with surface.shape (the protocol limits it to dock and menubar), so the strip uses
  // the plain .glass material. The host sends a full McSnapshot (mc.snapshot, then mc.changed while open); the grid diffs it and animates the change
  // with FLIP instead of re-rendering: new windows grow out of their real bounds, moved ones glide, closed ones fade.
  import { onMount, tick, untrack } from 'svelte';
  import { BridgeError, getBridge } from '$lib/ipc';
  import type { McSnapshot, McWindow, OverlayShow } from '$lib/protocol/generated';
  import { layoutWindows } from './layout';
  import { flipDelta, flipKeyframes, isNoopFlip, type Box } from './motion';
    import { reduceDrag, isDragging, IDLE, type DragState } from './dragState';
  import { applySnapshot, pickSelectedDesktop, visibleWindows, withWindowOnDesktop, withoutWindow } from './model';
  import { spatialNeighbor, firstInReadingOrder, navigateKey } from './navigate';
  import { t } from './strings';
  import DesktopStrip from './DesktopStrip.svelte';
  import WindowCard from './WindowCard.svelte';
  import AppIcon from '$lib/components/AppIcon.svelte';

  let { onclose }: { show: OverlayShow; onclose: () => void } = $props();

  const bridge = getBridge();

  /** Layout constants (DIP = CSS px; the overlay covers its monitor so viewport px are monitor DIPs). */
  const GAP = 28;
  const SIDE = 44;
  const BOTTOM = 52;
  const STRIP_TOP = 28;
  const STRIP_BLOCK = 136;
  const EASE = 'cubic-bezier(0.2, 0.8, 0.2, 1)';

  let snap = $state.raw<McSnapshot | null>(null);
  let selected = $state<string | null>(null);
  let vw = $state(window.innerWidth);
  let vh = $state(window.innerHeight);
  let toast = $state<string | null>(null);
  let drag = $state.raw<DragState>(IDLE);
  let ghosts = $state.raw<Array<{ key: number; win: McWindow; box: Box }>>([]);

  const hasStrip = $derived(snap !== null && snap.desktopsSupported && snap.desktops.length > 0);
  const gridTop = $derived(hasStrip ? STRIP_TOP + STRIP_BLOCK + 28 : 64);
  const area = $derived({ x: SIDE, y: gridTop, w: Math.max(1, vw - SIDE * 2), h: Math.max(1, vh - gridTop - BOTTOM) });
  const visible = $derived(snap ? visibleWindows(snap, selected) : []);

  /** Cards with their slots in viewport coordinates. `real` is the window's own box (the FLIP start for new cards). */
  const placed = $derived.by(() => {
    const list = visible;
    const slots = layoutWindows(list.map((w) => ({ id: w.handle, w: w.bounds.width, h: w.bounds.height })), area, GAP);
    const byId = new Map(list.map((w) => [w.handle, w] as const));
    return slots.map((s) => {
      const w = byId.get(s.id)!;
      return { id: s.id, win: w, box: { x: area.x + s.x, y: area.y + s.y, w: s.w, h: s.h } as Box, real: { x: w.bounds.x, y: w.bounds.y, w: w.bounds.width, h: w.bounds.height } as Box };
    });
  });

  const navSlots = $derived(placed.map((p) => ({ id: p.id, x: p.box.x, y: p.box.y, w: p.box.w, h: p.box.h })));

  // ---- FLIP ----------------------------------------------------------------------------------------------------------

  const cardEls = new Map<string, HTMLElement>();
  /** Last applied boxes and windows: the FLIP start for cards that move, and the source of the fade-out ghosts. */
  let lastBoxes = new Map<string, Box>();
  let lastWins = new Map<string, McWindow>();
  let ghostSeq = 0;

  function reduceMotion(): boolean {
    return document.documentElement.dataset.reduceMotion === 'true';
  }

  function registerCard(id: string, el: HTMLElement, on: boolean) {
    if (on) cardEls.set(id, el);
    else if (cardEls.get(id) === el) cardEls.delete(id);
  }

  /** Runs after each layout change (the DOM already has the new slots): animates moved and new cards, fades out removed ones. */
  function runFlip(current: typeof placed) {
    const animate = !reduceMotion();
    const nextBoxes = new Map<string, Box>();
    const nextWins = new Map<string, McWindow>();
    for (const p of current) {
      nextBoxes.set(p.id, p.box);
      nextWins.set(p.id, p.win);
      const el = cardEls.get(p.id);
      if (!animate || !el || typeof el.animate !== 'function') continue;
      const prev = lastBoxes.get(p.id);
      const from = prev ?? p.real; // a new window grows out of its real bounds, a moved one glides from its old slot
      if (isNoopFlip(flipDelta(from, p.box))) continue;
      el.animate(flipKeyframes(from, p.box), { duration: prev ? 280 : 340, easing: EASE });
    }
    if (animate) {
      const gone: Array<{ key: number; win: McWindow; box: Box }> = [];
      for (const [id, box] of lastBoxes) {
        if (!nextBoxes.has(id)) gone.push({ key: ghostSeq++, win: lastWins.get(id)!, box });
      }
      if (gone.length) ghosts = [...ghosts, ...gone];
    }
    lastBoxes = nextBoxes;
    lastWins = nextWins;
  }

  $effect(() => {
    const current = placed;
    untrack(() => runFlip(current));
  });

  function removeGhost(key: number) {
    ghosts = ghosts.filter((g) => g.key !== key);
  }

  // ---- data ----------------------------------------------------------------------------------------------------------

  let initialFocusDone = false;

  function apply(next: McSnapshot) {
    snap = applySnapshot(snap, next);
    if (snap) selected = pickSelectedDesktop(snap.desktops, selected);
    if (!initialFocusDone && snap && snap.windows.length) {
      initialFocusDone = true;
      void tick().then(focusInitial);
    }
  }

  async function refresh() {
    try {
      apply(await bridge.call('mc.snapshot', {}));
    } catch {
      showToast(t('mc.actionFailed'));
    }
  }

  function showToast(message: string) {
    toast = message;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => (toast = null), 3200);
  }
  let toastTimer: ReturnType<typeof setTimeout> | undefined;

  onMount(() => {
    const off = bridge.on('mc.changed', (s) => apply(s));
    void refresh();
    return () => {
      off();
      clearTimeout(toastTimer);
    };
  });

  // ---- actions -------------------------------------------------------------------------------------------------------

  /** A press that became a drag must not also activate the window on the click that follows. */
  let ignoreClick = false;
  function guardClick() {
    ignoreClick = true;
    setTimeout(() => (ignoreClick = false), 0);
  }

  async function activate(win: McWindow) {
    if (ignoreClick) return;
    try {
      await bridge.call('mc.activateWindow', { handle: win.handle });
      onclose();
    } catch {
      showToast(t('mc.actionFailed'));
      void refresh();
    }
  }

  async function closeWindow(win: McWindow, focusAfter?: string | null) {
    if (!snap) return;
    const before = snap;
    snap = withoutWindow(snap, win.handle);
    if (focusAfter !== undefined) void tick().then(() => focusCard(focusAfter));
    try {
      await bridge.call('mc.closeWindow', { handle: win.handle });
      void refresh();
    } catch {
      snap = before;
      showToast(t('mc.actionFailed'));
    }
  }

  async function switchDesktop(id: string) {
    try {
      await bridge.call('mc.switchDesktop', { desktopId: id });
      onclose();
    } catch {
      showToast(t('mc.actionFailed'));
    }
  }

  async function createDesktop() {
    try {
      await bridge.call('mc.createDesktop');
      await refresh();
    } catch {
      showToast(t('mc.actionFailed'));
    }
  }

  async function removeDesktop(id: string) {
    try {
      await bridge.call('mc.removeDesktop', { desktopId: id });
      await refresh();
    } catch {
      showToast(t('mc.actionFailed'));
    }
  }

  async function moveWindow(handle: string, desktopId: string) {
    try {
      await bridge.call('mc.moveWindow', { handle, desktopId });
      if (snap) snap = withWindowOnDesktop(snap, handle, desktopId);
      void refresh();
    } catch (e) {
      // 'unsupported': this Windows version refuses cross-desktop moves; the grid stays as it was.
      showToast(e instanceof BridgeError && e.code === 'unsupported' ? t('mc.moveUnsupported') : t('mc.moveFailed'));
    }
  }

  // ---- focus and keyboard --------------------------------------------------------------------------------------------

  function focusCard(id: string | null | undefined) {
    if (!id) return;
    const btn = cardEls.get(id)?.querySelector<HTMLElement>('button.hit');
    btn?.focus({ preventScroll: true });
  }

  function focusInitial() {
    const front = snap?.windows.find((w) => w.active && visible.some((v) => v.handle === w.handle));
    focusCard(front?.handle ?? firstInReadingOrder(navSlots));
  }

  function onKey(e: KeyboardEvent) {
    const target = e.target as HTMLElement;
    const cardId = target.dataset?.card;
    if (e.key === 'Tab') {
      const root = e.currentTarget as HTMLElement;
      const items = Array.from(root.querySelectorAll<HTMLElement>('[data-focusable]'));
      if (items.length === 0) return;
      const i = items.indexOf(document.activeElement as HTMLElement);
      if (e.shiftKey && i <= 0) { e.preventDefault(); items[items.length - 1]!.focus(); }
      else if (!e.shiftKey && (i === items.length - 1 || i === -1)) { e.preventDefault(); items[0]!.focus(); }
      return;
    }
    if (cardId !== undefined) {
      if (e.key === 'Delete') {
        e.preventDefault();
        const win = visible.find((w) => w.handle === cardId);
        if (win) {
          // focus the nearest remaining card after the removal
          const next = (['right', 'left', 'down', 'up'] as const).map((d) => spatialNeighbor(navSlots, cardId, d)).find((id) => id !== null) ?? null;
          void closeWindow(win, next);
        }
        return;
      }
      const dir = navigateKey(e.key);
      if (dir) {
        e.preventDefault();
        focusCard(spatialNeighbor(navSlots, cardId, dir) ?? cardId);
      }
      return;
    }
    if (target.dataset?.desktopId !== undefined && e.key === 'ArrowDown') {
      e.preventDefault();
      focusCard(firstInReadingOrder(navSlots));
    }
  }

  function onBackdropClick(e: MouseEvent) {
    if (e.target === e.currentTarget) onclose();
  }

  // ---- drag a window onto a desktop tile -----------------------------------------------------------------------------

  function desktopAt(x: number, y: number): string | null {
    const el = document.elementFromPoint(x, y) as HTMLElement | null;
    return el?.closest<HTMLElement>('[data-desktop-id]')?.dataset.desktopId ?? null;
  }

  function onPress(e: PointerEvent, win: McWindow) {
    drag = reduceDrag(drag, { type: 'down', handle: win.handle, sourceDesktopId: win.desktopId ?? null, x: e.clientX, y: e.clientY }).state;
  }

  function onPointerMove(e: PointerEvent) {
    if (drag.kind === 'idle') return;
    const was = drag;
    const r = reduceDrag(drag, { type: 'move', x: e.clientX, y: e.clientY, overDesktopId: desktopAt(e.clientX, e.clientY) });
    drag = r.state;
    if (!isDragging(was) && isDragging(r.state)) guardClick();
  }

  function onPointerUp() {
    if (drag.kind === 'idle') return;
    const was = isDragging(drag);
    const r = reduceDrag(drag, { type: 'up' });
    drag = r.state;
    if (was) guardClick();
    if (r.effect) void moveWindow(r.effect.handle, r.effect.desktopId);
  }

  function onPointerCancel() {
    drag = reduceDrag(drag, { type: 'cancel' }).state;
  }

  const dragHandle = $derived(drag.kind === 'idle' ? null : drag.handle);
  const dragWin = $derived(dragHandle === null ? null : (snap?.windows.find((w) => w.handle === dragHandle) ?? null));
  const dropId = $derived(drag.kind === 'dragging' ? drag.overDesktopId : null);
  const dropName = $derived(snap && dropId ? (snap.desktops.find((d) => d.id === dropId)?.name ?? null) : null);

  /** Status line under the grid when there is nothing to show. */
  const emptyText = $derived(
    snap === null ? '' : visible.length > 0 ? '' : hasStrip ? t('mc.emptyDesktop') : t('mc.empty'),
  );
</script>

<svelte:window
  bind:innerWidth={vw}
  bind:innerHeight={vh}
  onpointermove={onPointerMove}
  onpointerup={onPointerUp}
  onpointercancel={onPointerCancel}
/>

<!-- svelte-ignore a11y_no_noninteractive_element_interactions, a11y_click_events_have_key_events -->
<div
  class="mc"
  role="dialog"
  aria-modal="true"
  aria-label={t('mc.label')}
  tabindex="-1"
  onkeydown={onKey}
  onclick={onBackdropClick}
  oncontextmenu={(e) => { e.preventDefault(); onclose(); }}
>
  <div class="backdrop"></div>

  {#if hasStrip && snap}
    <DesktopStrip
      desktops={snap.desktops}
      windows={snap.windows}
      monitor={{ w: vw, h: vh }}
      selectedId={selected}
      {dropId}
      canRemove={snap.desktops.length > 1}
      onpick={(id) => (selected = id)}
      onswitch={switchDesktop}
      oncreate={createDesktop}
      onremove={removeDesktop}
    />
  {/if}

  {#each placed as p (p.id)}
    <WindowCard
      win={p.win}
      box={p.box}
      frontmost={p.win.active}
      onregister={registerCard}
      onpress={onPress}
      onactivate={activate}
      onclose={(w) => closeWindow(w, undefined)}
    />
  {/each}

  {#each ghosts as g (g.key)}
    <WindowCard win={g.win} box={g.box} ghost ondone={() => removeGhost(g.key)} />
  {/each}

  {#if snap === null}
    <p class="status" role="status">{t('mc.loading')}</p>
  {:else if emptyText}
    <p class="status" role="status">{emptyText}</p>
  {/if}

  {#if drag.kind === 'dragging' && dragWin}
    <div class="drag" style:transform="translate({drag.x + 14}px, {drag.y + 10}px)" aria-hidden="true">
      <AppIcon src={dragWin.iconUrl} name={dragWin.appName} size={18} radius={0.25} />
      <span class="drag-title">{dropName ? t('mc.moveTo', { name: dropName }) : dragWin.title}</span>
    </div>
  {/if}

  {#if toast}
    <div class="toast" role="status" aria-live="polite">{toast}</div>
  {/if}
</div>

<style>
  .mc {
    position: fixed;
    inset: 0;
    outline: none;
    user-select: none;
    cursor: default;
  }
  .backdrop {
    position: absolute;
    inset: 0;
    background: var(--backdrop-dim);
    animation: fade var(--dur) var(--ease-out);
  }
  @keyframes fade { from { opacity: 0; } to { opacity: 1; } }

  .status {
    position: absolute;
    left: 50%;
    top: 55%;
    transform: translate(-50%, -50%);
    margin: 0;
    padding: 8px 14px;
    border-radius: 999px;
    color: var(--text-2);
    background: var(--glass-bg-strong);
    border: 0.5px solid var(--glass-border);
  }

  .drag {
    position: fixed;
    left: 0;
    top: 0;
    z-index: 5;
    display: flex;
    align-items: center;
    gap: 8px;
    max-width: 320px;
    padding: 6px 10px 6px 7px;
    border-radius: 10px;
    pointer-events: none;
    color: var(--text);
    background: var(--glass-bg-strong);
    border: 0.5px solid var(--glass-border);
    box-shadow: var(--shadow-md);
    font-size: 12px;
    will-change: transform;
  }
  .drag-title { overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }

  .toast {
    position: absolute;
    left: 50%;
    bottom: 22px;
    transform: translateX(-50%);
    max-width: min(560px, calc(100vw - 48px));
    padding: 9px 16px;
    border-radius: 12px;
    color: var(--text);
    background: var(--glass-bg-strong);
    border: 0.5px solid var(--glass-border);
    box-shadow: var(--shadow-md);
    text-align: center;
    animation: rise var(--dur) var(--ease-out);
  }
  @keyframes rise { from { opacity: 0; transform: translate(-50%, 6px); } to { opacity: 1; transform: translate(-50%, 0); } }
</style>
