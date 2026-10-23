<script lang="ts">
  import { onMount } from 'svelte';
  import { BridgeError, getBridge, getInit } from '$lib/ipc';
  import { hostState } from '$lib/ipc/state.svelte';
  import { glassShape, hitShape } from '$lib/shape';
  import AppIcon from '$lib/components/AppIcon.svelte';
  import type { DockBounceKind, Rect } from '$lib/protocol/generated';
  import ItemView from './ItemView.svelte';
  import TileView from './TileView.svelte';
  import { t, windowsCount } from './strings';
  import {
    applyFrame, crossOf, isTouch, mainOf, originOf, outsideOf, popoverAnchor, slotCenterClient, slotStyle, tooltipPlacement,
    type RectLike, type SlotRefs,
  } from './dom';
  import { fitIconSize, layoutStrip, magnifySpec, metricsFor, pillThickness, windowSizing, type DockMode, type DockPos, type SlotSpec } from './layout';
  import { appIdOf, buildEntries, dragKind, emptyDockState, opensWindowList, pinnedAppIndices, pinnedIds, reorderGroup, type Entry } from './model';
  import { DockMotion, type Frame } from './motion';
  import { exceedsDragThreshold, planDrag, reorderedPinned, sameOrder, unpinDistance, type DragKind, type DragPlan, type DragVerdict } from './reorder';
  import { hopAmplitude } from './hop';

  const bridge = getBridge();
  const init = getInit();
  /** Host can keep magnified icons / tooltips un-blurred outside the pill? If not, everything must stay inside glass (the pill grows). */
  const glassOverflow = init.glassOverflow;

  const dock = hostState('dock.state', emptyDockState());
  const settings = hostState('settings.changed', init.settings);
  const theme = hostState('theme.changed', init.theme);

  // ------------------------------------------------------------------ settings-derived geometry
  const cfg = $derived(settings.value.dock);
  const pos = $derived<DockPos>(cfg.position === 'left' || cfg.position === 'right' ? cfg.position : 'bottom');
  const reduceMotion = $derived(theme.value.reduceMotion);
  let touchMode = $state(false);
  const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, Number.isFinite(v) ? v : lo));
  const reqSize = $derived(clamp(Math.round(cfg.iconSize), 32, 128));
  const reqMag = $derived(clamp(Math.round(cfg.magnifiedSize), reqSize, 192));
  const magOn = $derived(cfg.magnification && !reduceMotion && !touchMode && reqMag > reqSize);

  let optimisticOrder = $state<string[] | null>(null);
  const entries = $derived(buildEntries(dock.value, optimisticOrder));
  const specs = $derived(entries.map((e): SlotSpec => ({ key: e.key, kind: e.kind === 'item' ? 'item' : e.kind })));
  const monitorMain = $derived(((pos === 'bottom' ? init.monitor.boundsPx.width : init.monitor.boundsPx.height) || 1920) / (init.monitor.scale || 1));
  const size = $derived(fitIconSize(reqSize, monitorMain, specs, pos, reqMag, magOn));
  const m = $derived(metricsFor(size));
  const strip = $derived(layoutStrip(specs, m, pos));
  const mag = $derived(magnifySpec(m, magOn, reqSize, reqMag));
  const T = $derived(pillThickness(m));
  const stageW = $derived(pos === 'bottom' ? strip.length + 2 * m.pad : T);
  const stageH = $derived(pos === 'bottom' ? T : strip.length + 2 * m.pad);
  const emptyPad = $derived(entries.length === 0 ? Math.round(m.size * 0.5) : 0);
  const byKey = $derived(new Map(entries.map((e, i) => [e.key, i] as const)));
  const ext = $derived(pos === 'bottom' ? `0 ${-m.gap / 2}px` : `${-m.gap / 2}px 0`);
  const extIn = $derived(pos === 'bottom' ? `0 ${m.gap / 2}px` : `${m.gap / 2}px 0`);

  // ------------------------------------------------------------------ DOM refs and the animation engine
  let rootEl = $state<HTMLElement>();
  let stageEl = $state<HTMLElement>();
  let pillEl = $state<HTMLElement>();
  let tipEl = $state<HTMLElement>();
  const refs = new Map<string, SlotRefs>();
  const motion = new DockMotion();
  let lastFrame: Frame | null = null;
  let stageRect: RectLike = { left: 0, top: 0, right: 0, bottom: 0 };
  let raf = 0;
  let lastTick = 0;

  /** Svelte action: registers a slot (its first child is the scaled `.mag` element). */
  function track(node: HTMLElement, key: string) {
    refs.set(key, { slot: node, mag: node.firstElementChild as HTMLElement | null });
    return { destroy() { if (refs.get(key)?.slot === node) refs.delete(key); } };
  }

  function refreshRect() {
    if (!stageEl) return;
    const r = stageEl.getBoundingClientRect();
    stageRect = { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
  }

  function wake() {
    if (raf) return;
    lastTick = performance.now();
    raf = requestAnimationFrame(tick);
  }
  function tick(now: number) {
    raf = 0;
    const dt = Math.min(0.05, Math.max(0, (now - lastTick) / 1000));
    lastTick = now;
    const busy = motion.step(dt);
    render();
    if (busy || pointerInside || dragging) { raf = requestAnimationFrame(tick); lastTick = now; }
  }
  function render() {
    const f = motion.frame();
    lastFrame = f;
    applyFrame(f, strip.slots, refs, pillEl ?? null, pos, m, !glassOverflow);
    if (!dragging) setHover(f.hover >= 0 ? strip.slots[f.hover]?.key ?? null : null);
    placeTip(f);
  }

  $effect(() => {
    motion.configure(strip.slots, strip.length, m, mag, reduceMotion);
    motion.setBounce(!glassOverflow && hop > 0 ? hop : 0);
    wake();
  });

  // drag state (declared early: the sizing and tooltip logic read it)
  interface Press { key: string; index: number; kind: DragKind | null; x: number; y: number; grabX: number; grabY: number; pointerId: number; type: string; timer?: ReturnType<typeof setTimeout> }
  interface Drag { key: string; index: number; kind: DragKind; plan: DragPlan; x: number; y: number; settle: null | 'land' | 'return' | 'poof' }
  let press: Press | null = null;
  let drag = $state<Drag | null>(null);
  let hiddenKey = $state<string | null>(null);
  let holdTimer: ReturnType<typeof setTimeout> | undefined;
  let settleTimer: ReturnType<typeof setTimeout> | undefined;
  const dragging = $derived(drag !== null);

  // ------------------------------------------------------------------ window sizing (the UI owns the window size, docs/IPC.md section 5)
  let mode = $state<DockMode>('idle');
  let modeTimer: ReturnType<typeof setTimeout> | undefined;
  const rank = (x: DockMode) => (x === 'idle' ? 0 : x === 'hover' ? 1 : 2);
  function wantMode(next: DockMode) {
    clearTimeout(modeTimer);
    if (rank(next) >= rank(mode)) { mode = next; return; }
    modeTimer = setTimeout(() => { mode = next; }, 280); // keep the headroom until the icons have settled
  }

  let bounces = $state<Record<string, { kind: DockBounceKind; n: number }>>({});
  let bounceSeq = 0;
  const bouncing = $derived(Object.keys(bounces).length > 0 || entries.some((e) => e.kind === 'item' && ((e.item.launching && cfg.bounceOnLaunch) || e.item.attention)));
  const hop = $derived(bouncing && !reduceMotion ? hopAmplitude(m.size) : 0);

  let revealed = $state(true);
  let slid = $state(false);
  let collapsed = $state(false);
  const autoHide = $derived(cfg.autoHide);

  let dragMode = $state<DragKind | null>(null);
  const sizing = $derived(windowSizing({
    pos, strip, m, mag, mode, hop, dragExtra: dragMode === 'pin' ? m.size + m.gap : 0, monitorMain, collapsed: autoHide && collapsed,
  }));

  let sentKey = '';
  let resizeRaf = 0;
  $effect(() => {
    const s = sizing;
    cancelAnimationFrame(resizeRaf);
    resizeRaf = requestAnimationFrame(() => {
      const key = `${s.width}x${s.height}/${s.barThickness}`;
      if (key === sentKey) return;
      sentKey = key;
      bridge.emit('surface.resize', { width: s.width, height: s.height, barThickness: s.barThickness });
      queueMicrotask(refreshRect);
    });
  });

  // ------------------------------------------------------------------ auto-hide: the host owns edge detection, we animate
  let prevAutoHide: boolean | null = null;
  $effect(() => {
    const on = autoHide;
    if (prevAutoHide === null) revealed = !on; // start slid out; the host reveals when the pointer touches the edge
    else if (on !== prevAutoHide) revealed = !on;
    prevAutoHide = on;
  });
  $effect(() => {
    const hide = autoHide && !revealed;
    let timer: ReturnType<typeof setTimeout> | undefined;
    if (hide) {
      slid = true;
      timer = setTimeout(() => { collapsed = true; }, reduceMotion ? 0 : 420);
    } else {
      collapsed = false;
      // give the host a moment to grow the window before sliding in, otherwise the first frames are clipped
      timer = setTimeout(() => { slid = false; }, slid ? 70 : 0);
    }
    return () => clearTimeout(timer);
  });

  // ------------------------------------------------------------------ hover, tooltip, pointer
  let pointerInside = false;
  let hoverKey = $state<string | null>(null);
  let kbKey = $state<string | null>(null);
  let tipOpen = $state(false);
  let tipTimer: ReturnType<typeof setTimeout> | undefined;
  let tipWarmUntil = 0;
  let tipW = $state(0);
  let tipH = $state(0);
  const tipKey = $derived(hoverKey ?? kbKey);

  function setHover(key: string | null) {
    if (key === hoverKey) return;
    hoverKey = key;
    clearTimeout(tipTimer);
    if (!key) {
      if (tipOpen) tipWarmUntil = performance.now() + 350;
      tipOpen = false;
      return;
    }
    if (tipOpen || performance.now() < tipWarmUntil) { tipOpen = true; return; }
    tipTimer = setTimeout(() => { if (hoverKey && !press && !dragging) tipOpen = true; }, 120);
  }

  const tipText = $derived.by(() => {
    const i = tipKey ? byKey.get(tipKey) : undefined;
    const e = i === undefined ? undefined : entries[i];
    if (!e) return '';
    if (e.kind === 'tile') return e.win.title;
    if (e.kind === 'item') return e.item.launching ? `${e.item.name} · ${t('dock.launching')}` : e.item.name;
    return '';
  });
  const showTip = $derived((tipOpen || kbKey !== null) && !!tipText && !dragging && !slid);

  function placeTip(f: Frame) {
    if (!tipEl || !tipKey) return;
    const i = byKey.get(tipKey);
    if (i === undefined) return;
    const s = strip.slots[i];
    const fs = f.slots[i];
    const lo = 6;
    const minMain = pos === 'bottom' ? lo - stageRect.left : lo - stageRect.top;
    const maxMain = pos === 'bottom' ? innerWidth - lo - stageRect.left : innerHeight - lo - stageRect.top;
    const p = tooltipPlacement({ pos, m, slot: s, offset: fs.offset, scale: fs.scale, grow: f.grow, pillGrows: !glassOverflow, tipW, tipH, minMain, maxMain });
    tipEl.style.translate = `${Math.round(p.x * 10) / 10}px ${Math.round(p.y * 10) / 10}px`;
  }
  $effect(() => { void showTip; void tipText; void tipW; void tipH; void tipEl; if (lastFrame) placeTip(lastFrame); });

  function onMove(e: PointerEvent) {
    if (isTouch(e)) { if (!touchMode) touchMode = true; } else if (touchMode) touchMode = false;
    refreshRect();
    if (press || drag) { dragMove(e); return; }
    if (isTouch(e)) return;
    const main = mainOf(pos, stageRect, m, e.clientX, e.clientY);
    const cross = crossOf(pos, stageRect, e.clientX, e.clientY);
    const f = lastFrame;
    const topExtent = T + (f?.grow ?? 0);
    const inside = cross >= -m.edgeGap && cross <= topExtent + 3 && main >= (f?.pillStart ?? -m.pad) - 3 && main <= (f?.pillEnd ?? strip.length + m.pad) + 3;
    if (inside) {
      if (!pointerInside) { pointerInside = true; wantMode('hover'); }
      motion.setPointer(main);
      wake();
    } else leaveHover();
  }
  function leaveHover() {
    if (!pointerInside) return;
    pointerInside = false;
    motion.setPointer(null);
    setHover(null);
    if (!dragging) wantMode('idle');
    wake();
  }

  // ------------------------------------------------------------------ calls
  function log(level: 'warn' | 'error', message: string) { bridge.emit('surface.log', { level, message }); }
  async function run(p: Promise<unknown>, what: string): Promise<boolean> {
    try { await p; return true; } catch (e) {
      if (!(e instanceof BridgeError && e.code === 'unsupported')) log('warn', `${what} failed: ${e instanceof Error ? e.message : String(e)}`);
      return false;
    }
  }

  function itemOf(key: string) {
    const i = byKey.get(key);
    return i === undefined ? undefined : entries[i];
  }

  function openPopover(kind: 'dockMenu' | 'windowList', key: string) {
    const i = byKey.get(key);
    if (i === undefined) return;
    refreshRect();
    const fs = lastFrame?.slots[i];
    const anchor: Rect = popoverAnchor(pos, stageRect, m, strip.slots[i], fs?.offset ?? 0, fs?.scale ?? 1);
    setHover(null);
    tipOpen = false;
    void run(bridge.call('overlay.open', { mode: 'popover', popover: { kind, anchor, itemId: itemIdOf(entries[i]) } }), `open ${kind}`);
  }
  function itemIdOf(e: Entry): string { return e.kind === 'item' ? e.item.id : e.kind === 'tile' ? e.win.appId : e.key; }

  let suppressClick = false;
  function onClick(e: MouseEvent) {
    if (suppressClick) { suppressClick = false; e.preventDefault(); return; }
    const key = (e.target as Element).closest<HTMLElement>('[data-key]')?.dataset.key;
    const entry = key ? itemOf(key) : undefined;
    if (!entry) return;
    tipOpen = false;
    if (entry.kind === 'tile') void run(bridge.call('dock.activateWindow', { handle: entry.win.handle }), 'activate window');
    else if (entry.kind === 'item') {
      if (opensWindowList(entry.item)) openPopover('windowList', entry.key);
      else void run(bridge.call('dock.activate', { itemId: entry.item.id }), 'activate');
    }
  }
  function onContext(e: MouseEvent) {
    e.preventDefault();
    const key = (e.target as Element).closest<HTMLElement>('[data-key]')?.dataset.key;
    const entry = key ? itemOf(key) : undefined;
    if (entry?.kind === 'item') openPopover('dockMenu', entry.key);
  }

  // ------------------------------------------------------------------ keyboard
  let focusKey = $state<string | null>(null);
  const tabKey = $derived(focusKey && byKey.has(focusKey) ? focusKey : entries.find((e) => e.kind !== 'sep')?.key ?? null);
  function onKey(e: KeyboardEvent) {
    const el = (e.target as Element).closest<HTMLElement>('[data-key]');
    const key = el?.dataset.key;
    if (!el || !key) return;
    if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      const entry = itemOf(key);
      if (entry?.kind === 'item') { e.preventDefault(); openPopover('dockMenu', key); }
      return;
    }
    const horizontal = pos === 'bottom';
    const next = (horizontal ? { ArrowRight: 1, ArrowLeft: -1 } : { ArrowDown: 1, ArrowUp: -1 }) as Record<string, number>;
    const step = next[e.key] ?? (e.key === 'Home' ? -Infinity : e.key === 'End' ? Infinity : 0);
    if (!step) return;
    e.preventDefault();
    const all = [...(el.closest('.strip')?.querySelectorAll<HTMLElement>('button[data-key]') ?? [])];
    const at = all.indexOf(el);
    const to = step === -Infinity ? 0 : step === Infinity ? all.length - 1 : Math.min(all.length - 1, Math.max(0, at + step));
    all[to]?.focus();
  }
  function onFocusIn(e: FocusEvent) {
    const el = e.target as HTMLElement;
    const key = el.dataset?.key;
    if (!key) return;
    focusKey = key;
    kbKey = el.matches(':focus-visible') ? key : null;
    if (kbKey) wake();
  }
  function onFocusOut() { kbKey = null; }

  // ------------------------------------------------------------------ drag & drop: reorder, pin by dragging, unpin by dragging out

  function onDown(e: PointerEvent) {
    if (e.button !== 0) return;
    suppressClick = false;
    const el = (e.target as Element).closest<HTMLElement>('[data-key]');
    const key = el?.dataset.key;
    if (!el || !key || drag) return;
    refreshRect();
    const index = byKey.get(key);
    if (index === undefined) return;
    const entry = entries[index];
    const c = slotCenterClient(pos, stageRect, m, strip.slots[index], lastFrame?.slots[index]?.offset ?? 0);
    const sc = lastFrame?.slots[index]?.scale ?? 1;
    press = {
      key, index, kind: dragKind(entry), x: e.clientX, y: e.clientY, pointerId: e.pointerId, type: e.pointerType,
      grabX: (e.clientX - c.x) / sc, grabY: (e.clientY - c.y) / sc,
    };
    tipOpen = false;
    clearTimeout(tipTimer);
    try { el.setPointerCapture(e.pointerId); } catch { /* not capturable (synthetic event): pointer events still bubble */ }
    if (isTouch(e) && entry.kind === 'item') {
      const p = press;
      p.timer = setTimeout(() => { if (press === p && !drag) { suppressClick = true; press = null; openPopover('dockMenu', key); } }, 550);
    }
  }

  function startDrag(e: PointerEvent) {
    const p = press!;
    clearTimeout(p.timer);
    if (!p.kind) return;
    dragMode = p.kind;
    drag = { key: p.key, index: p.index, kind: p.kind, plan: { verdict: 'cancel', insertIndex: -1, shifts: [], lengthDelta: 0 }, x: e.clientX, y: e.clientY, settle: null };
    hiddenKey = p.key;
    if (pointerInside) { pointerInside = false; motion.setPointer(null); setHover(null); }
    wantMode('drag');
    suppressClick = true;
    wake();
  }

  function planFor(e: PointerEvent): DragPlan {
    const d = drag!, p = press!;
    const main = mainOf(pos, stageRect, m, e.clientX - p.grabX, e.clientY - p.grabY);
    const group = d.kind === 'reorder' ? reorderGroup(entries, d.index) : pinnedAppIndices(entries);
    return planDrag(
      { kind: d.kind, dragged: d.index, group, centers: strip.slots.map((s) => s.start + s.len / 2), slot: m.size + m.gap },
      { main, outside: outsideOf(pos, stageRect, m, e.clientX, e.clientY) },
      unpinDistance(m.size),
    );
  }

  function dragMove(e: PointerEvent) {
    if (!press) return;
    if (!drag) {
      if (!exceedsDragThreshold(e.clientX - press.x, e.clientY - press.y)) return;
      startDrag(e);
      if (!drag) { press = null; return; }
    }
    if (!drag || drag.settle) return;
    const plan = planFor(e);
    drag = { ...drag, x: e.clientX - press.grabX, y: e.clientY - press.grabY, plan };
    motion.setDrag(plan.shifts, plan.lengthDelta);
    wake();
  }

  function onUp(e: PointerEvent) {
    if (press && !drag) { clearTimeout(press.timer); press = null; return; }
    if (!press || !drag || drag.settle) return;
    refreshRect();
    finishDrag(planFor(e));
  }
  function onCancel() {
    if (press && !drag) { clearTimeout(press.timer); press = null; }
    else if (drag && !drag.settle) abortDrag();
  }

  function settleIn(ms: number, how: Drag['settle']) {
    if (!drag) return;
    drag = { ...drag, settle: how };
    clearTimeout(settleTimer);
    settleTimer = setTimeout(endDrag, ms);
  }

  function finishDrag(plan: DragPlan) {
    const d = drag!;
    const entry = entries[d.index];
    if (entry.kind !== 'item') { abortDrag(); return; }
    const item = entry.item;
    if (plan.verdict === 'reorder') {
      const all = pinnedIds(dock.value.items);
      const order = reorderedPinned(all, reorderGroup(entries, d.index).map((i) => (entries[i] as { key: string }).key), item.id, plan.insertIndex);
      const to = slotCenterClient(pos, stageRect, m, strip.slots[d.index], plan.shifts[d.index]);
      drag = { ...d, plan, x: to.x, y: to.y };
      settleIn(210, 'land');
      if (!sameOrder(all, order)) {
        optimisticOrder = order;
        holdShifts();
        const sent = order;
        void run(bridge.call('dock.reorder', { itemIds: sent }), 'reorder').then((ok) => { if (!ok) { optimisticOrder = null; } optimisticDone = true; clearOptimisticIfSettled(); });
      }
      return;
    }
    if (plan.verdict === 'pin') {
      const to = slotCenterClient(pos, stageRect, m, strip.slots[d.index], plan.shifts[d.index]);
      drag = { ...d, plan, x: to.x, y: to.y };
      settleIn(210, 'land');
      holdShifts();
      void run(bridge.call('dock.pin', { appId: appIdOf(item), index: plan.insertIndex }), 'pin');
      return;
    }
    if (plan.verdict === 'unpin') {
      drag = { ...d, plan };
      settleIn(420, 'poof');
      holdShifts();
      void run(bridge.call('dock.unpin', { itemId: item.id }), 'unpin');
      return;
    }
    const home = slotCenterClient(pos, stageRect, m, strip.slots[d.index], 0);
    drag = { ...d, plan, x: home.x, y: home.y };
    motion.setDrag(strip.slots.map(() => 0), 0);
    settleIn(190, 'return');
  }

  /** After the drop the layout is authoritative once dock.state arrives; until then keep the preview shifts so nothing jumps back. */
  function holdShifts() {
    clearTimeout(holdTimer);
    holdTimer = setTimeout(() => { holdTimer = undefined; motion.setDrag(null); hiddenKey = null; wake(); }, 1000);
  }
  function releaseHold() {
    if (holdTimer === undefined) return;
    clearTimeout(holdTimer);
    holdTimer = undefined;
    motion.resetShifts();
    if (!drag) hiddenKey = null; // otherwise the landing ghost still covers the slot
    render();
  }

  function endDrag() {
    clearTimeout(settleTimer);
    drag = null;
    press = null;
    dragMode = null;
    // a pending hold (waiting for dock.state) keeps the preview shifts and the hidden source icon until it is released
    if (holdTimer === undefined) { motion.setDrag(null); hiddenKey = null; }
    wantMode(pointerInside ? 'hover' : 'idle');
    wake();
  }
  function abortDrag() {
    if (!drag) return;
    const home = slotCenterClient(pos, stageRect, m, strip.slots[drag.index], 0);
    motion.setDrag(strip.slots.map(() => 0), 0);
    drag = { ...drag, x: home.x, y: home.y };
    settleIn(190, 'return');
  }

  // layout keys changed under a running drag (window closed, state pushed): the preview is stale
  let lastKeys = '';
  $effect(() => {
    const keys = entries.map((e) => e.key).join('|');
    if (keys === lastKeys) return;
    lastKeys = keys;
    if (drag && !drag.settle) { abortDrag(); return; }
    releaseHold();
  });

  // optimistic pinned order (right after a reorder drop) is dropped once the host confirms it
  let optimisticDone = false;
  let optimisticTimer: ReturnType<typeof setTimeout> | undefined;
  function clearOptimisticIfSettled() {
    if (optimisticOrder && optimisticDone) { optimisticOrder = null; optimisticDone = false; clearTimeout(optimisticTimer); }
  }
  $effect(() => {
    const s = dock.value;
    if (!optimisticOrder) return;
    if (sameOrder(pinnedIds(s.items), optimisticOrder) || optimisticDone) { optimisticOrder = null; optimisticDone = false; return; }
    clearTimeout(optimisticTimer);
    optimisticTimer = setTimeout(() => { optimisticOrder = null; optimisticDone = false; }, 1500);
  });

  // ------------------------------------------------------------------ host events
  onMount(() => {
    const offBounce = bridge.on('dock.bounce', (b) => {
      if (b.kind === 'launch' && !cfg.bounceOnLaunch) return;
      const n = ++bounceSeq;
      bounces = { ...bounces, [b.itemId]: { kind: b.kind, n } };
      setTimeout(() => { if (bounces[b.itemId]?.n === n) { const { [b.itemId]: _gone, ...rest } = bounces; bounces = rest; } }, b.kind === 'launch' ? 1100 : 2600);
    });
    const offReveal = bridge.on('dock.reveal', (r) => { if (autoHide) revealed = r.revealed; });
    const leave = () => { leaveHover(); };
    const onOut = (e: MouseEvent) => { if (!e.relatedTarget) leave(); };
    document.documentElement.addEventListener('mouseleave', leave);
    window.addEventListener('mouseout', onOut);
    window.addEventListener('blur', leave);
    const ro = new ResizeObserver(refreshRect);
    if (rootEl) ro.observe(rootEl);
    window.addEventListener('resize', refreshRect);
    refreshRect();
    return () => {
      offBounce(); offReveal(); ro.disconnect();
      document.documentElement.removeEventListener('mouseleave', leave);
      window.removeEventListener('mouseout', onOut);
      window.removeEventListener('blur', leave);
      window.removeEventListener('resize', refreshRect);
      cancelAnimationFrame(raf); cancelAnimationFrame(resizeRaf);
      for (const x of [modeTimer, tipTimer, holdTimer, settleTimer, optimisticTimer]) clearTimeout(x);
      dock.dispose(); settings.dispose(); theme.dispose();
    };
  });

  function onWindowKey(e: KeyboardEvent) {
    if (e.key === 'Escape' && drag && !drag.settle) { e.preventDefault(); abortDrag(); }
  }

  // ------------------------------------------------------------------ view helpers
  function labelOf(e: Entry): string {
    if (e.kind === 'tile') return `${e.win.title}, ${t('dock.minimized')}`;
    if (e.kind !== 'item') return '';
    const it = e.item;
    const parts = [it.name];
    if (it.launching) parts.push(t('dock.launching'));
    else if (it.kind === 'folder') parts.push(t('dock.folder'));
    else if (it.running) parts.push(it.windows.length > 1 ? windowsCount(it.windows.length) : t('dock.running'));
    if (it.attention) parts.push(t('dock.attention'));
    return parts.join(', ');
  }
  const loopOf = (e: Entry): 'launching' | 'attention' | null =>
    e.kind !== 'item' || reduceMotion ? null : e.item.attention ? 'attention' : e.item.launching && cfg.bounceOnLaunch ? 'launching' : null;
  const hitMode = $derived(glassOverflow && mode !== 'idle');
  const ghostItem = $derived(drag ? entries[drag.index] : undefined);
  const ghostSize = $derived(m.size + 12);
</script>

<svelte:window onkeydown={onWindowKey} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="root {pos}"
  class:overflow={glassOverflow}
  bind:this={rootEl}
  onpointermove={onMove}
  onpointerdown={onDown}
  onpointerup={onUp}
  onpointercancel={onCancel}
  style:--gap="{m.gap}px"
  style:--ext={ext}
  style:--ext-in={extIn}
  style:--edge="{m.edgeGap}px"
  style:--pad="{m.pad}px"
>
  <div
    class="stage"
    class:slid
    class:gone={autoHide && collapsed}
    bind:this={stageEl}
    style:width="{stageW + emptyPad}px"
    style:height="{stageH}px"
  >
    <div class="pill glass" bind:this={pillEl} use:glassShape={{ radius: m.radius }} style:border-radius="{m.radius}px"></div>
    <div
      class="strip"
      role="toolbar"
      aria-label={t('dock.label')}
      aria-orientation={pos === 'bottom' ? 'horizontal' : 'vertical'}
      tabindex="-1"
      onclick={onClick}
      oncontextmenu={onContext}
      onkeydown={onKey}
      onfocusin={onFocusIn}
      onfocusout={onFocusOut}
    >
      {#each entries as e, i (e.key)}
        {@const s = strip.slots[i]}
        {#if s}
          <div class="slot {e.kind}" use:track={e.key} style={slotStyle(pos, m, s)}>
            <div class="mag" style:transform-origin={originOf(pos)}>
              {#if e.kind === 'item'}
                <ItemView
                  item={e.item} size={m.size} {pos} label={labelOf(e)} tabbable={tabKey === e.key}
                  hidden={hiddenKey === e.key} loop={loopOf(e)} burst={bounces[e.item.id]?.kind === 'launch' ? bounces[e.item.id].n : 0}
                  motionOk={!reduceMotion} hit={hitMode}
                />
              {:else if e.kind === 'tile'}
                <TileView win={e.win} w={pos === 'bottom' ? s.len : s.cross} h={pos === 'bottom' ? s.cross : s.len} label={labelOf(e)} tabbable={tabKey === e.key} hit={hitMode} />
              {:else}
                <div class="line"></div>
              {/if}
            </div>
            {#if e.kind === 'item' && cfg.showIndicators && e.item.running && !e.item.launching && hiddenKey !== e.key}
              <i class="dot" class:on={e.item.active} aria-hidden="true"></i>
            {/if}
          </div>
        {/if}
      {/each}
    </div>

    {#if showTip}
      {#if glassOverflow}
        <div class="tip tip-hit" role="tooltip" bind:this={tipEl} bind:offsetWidth={tipW} bind:offsetHeight={tipH} use:hitShape={{ radius: 9 }}>{tipText}</div>
      {:else}
        <div class="tip glass" role="tooltip" bind:this={tipEl} bind:offsetWidth={tipW} bind:offsetHeight={tipH} use:glassShape={{ radius: 9 }}>{tipText}</div>
      {/if}
    {/if}
  </div>

  {#if drag && ghostItem?.kind === 'item'}
    <div
      class="ghost"
      class:settle={drag.settle !== null}
      class:poof={drag.settle === 'poof'}
      style:width="{ghostSize}px"
      style:height="{ghostSize}px"
      style:translate="{drag.x - ghostSize / 2}px {drag.y - ghostSize / 2}px"
    >
      {#if drag.plan.verdict === 'unpin' && drag.settle !== 'poof'}
        {#if glassOverflow}
          <div class="remove remove-hit" use:hitShape={{ radius: 8 }}>{t('dock.remove')}</div>
        {:else}
          <div class="remove glass" use:glassShape={{ radius: 8 }}>{t('dock.remove')}</div>
        {/if}
      {/if}
      {#if glassOverflow}
        <div class="lift lift-hit" use:hitShape={{ radius: 16 }}><AppIcon src={ghostItem.item.iconUrl} name={ghostItem.item.name} size={m.size} /></div>
      {:else}
        <div class="lift glass" use:glassShape={{ radius: 16 }}><AppIcon src={ghostItem.item.iconUrl} name={ghostItem.item.name} size={m.size} /></div>
      {/if}
      {#if drag.settle === 'poof' && glassOverflow}
        {#each [0, 1, 2, 3, 4, 5, 6, 7] as n (n)}<i class="puff" style:--a="{n * 45 + 12}deg"></i>{/each}
      {/if}
    </div>
  {/if}
</div>

<style>
  .root { position: fixed; inset: 0; display: flex; overflow: hidden; touch-action: none; }
  .root.bottom { justify-content: center; align-items: flex-end; padding-bottom: var(--edge); }
  .root.left { justify-content: flex-start; align-items: center; padding-left: var(--edge); }
  .root.right { justify-content: flex-end; align-items: center; padding-right: var(--edge); }

  .stage { position: relative; flex: none; transition: transform var(--dur-slow) var(--ease-out); }
  .stage.gone { display: none; }
  .bottom .stage.slid { transform: translate3d(0, calc(100% + var(--edge) + 24px), 0); pointer-events: none; }
  .left .stage.slid { transform: translate3d(calc(-100% - var(--edge) - 24px), 0, 0); pointer-events: none; }
  .right .stage.slid { transform: translate3d(calc(100% + var(--edge) + 24px), 0, 0); pointer-events: none; }

  .pill { position: absolute; left: 0; top: 0; width: 100%; height: 100%; }
  .strip { position: absolute; inset: 0; outline: none; }
  .slot { position: absolute; }
  .mag { position: absolute; inset: 0; }
  .line { position: absolute; background: var(--separator); border-radius: 1px; }
  .bottom .line { left: 50%; top: 0; bottom: 0; width: 1px; margin-left: -0.5px; }
  .left .line, .right .line { top: 50%; left: 0; right: 0; height: 1px; margin-top: -0.5px; }

  .dot {
    position: absolute; width: 4px; height: 4px; border-radius: 50%;
    background: var(--text); opacity: 0.5; pointer-events: none;
    transition: opacity var(--dur) var(--ease-out), transform var(--dur) var(--ease-spring);
  }
  .dot.on { opacity: 0.95; transform: scale(1.25); box-shadow: 0 0 5px color-mix(in srgb, var(--accent) 70%, transparent); }
  .bottom .dot { left: 50%; bottom: calc(var(--pad) / -2 - 2px); margin-left: -2px; }
  .left .dot { top: 50%; left: calc(var(--pad) / -2 - 2px); margin-top: -2px; }
  .right .dot { top: 50%; right: calc(var(--pad) / -2 - 2px); margin-top: -2px; }

  .tip {
    position: absolute; left: 0; top: 0; padding: 5px 11px; border-radius: 9px; white-space: nowrap; max-width: 320px; overflow: hidden; text-overflow: ellipsis;
    font-size: 12.5px; font-weight: 500; line-height: 1.3; letter-spacing: 0.005em; pointer-events: none;
    animation: tip-in var(--dur-fast) var(--ease-out); z-index: 3;
  }
  .tip-hit { background: var(--glass-bg-strong); border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-md); }
  @keyframes tip-in { from { opacity: 0; transform: scale(0.94); } to { opacity: 1; transform: scale(1); } }

  .ghost { position: fixed; left: 0; top: 0; z-index: 5; pointer-events: none; display: grid; place-items: center; }
  .ghost.settle { transition: translate 190ms var(--ease-out); }
  .lift { width: 100%; height: 100%; border-radius: 16px; display: grid; place-items: center; transform: scale(1.06); box-shadow: var(--shadow-md); }
  .lift-hit { background: none; box-shadow: none; filter: drop-shadow(0 6px 10px rgba(0, 0, 0, 0.35)); }
  .remove {
    position: absolute; left: 50%; bottom: calc(100% + 8px); transform: translateX(-50%); padding: 3px 10px; border-radius: 8px; font-size: 12px; font-weight: 500; white-space: nowrap;
    animation: tip-in var(--dur-fast) var(--ease-out);
  }
  .remove-hit { background: var(--glass-bg-strong); border: 0.5px solid var(--glass-border); box-shadow: var(--shadow-md); }
  .ghost.poof .lift { animation: poof 380ms var(--ease-out) forwards; }
  @keyframes poof { to { transform: scale(0.2); opacity: 0; filter: blur(8px); } }
  .puff {
    position: absolute; left: 50%; top: 50%; width: 9px; height: 9px; margin: -4.5px 0 0 -4.5px; border-radius: 50%;
    background: radial-gradient(circle, rgba(255, 255, 255, 0.95), rgba(255, 255, 255, 0.35)); filter: blur(1.5px);
    animation: puff 420ms var(--ease-out) forwards;
  }
  @keyframes puff {
    from { translate: 0 0; scale: 0.4; opacity: 0.95; }
    to { translate: calc(cos(var(--a)) * 38px) calc(sin(var(--a)) * 38px); scale: 1.5; opacity: 0; }
  }
</style>
