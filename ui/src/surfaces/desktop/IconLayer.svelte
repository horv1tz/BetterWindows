<script lang="ts">
  // The icon grid: placement, selection (click / Ctrl / Shift / marquee / keyboard), drag-to-move with grid snapping and a ghost.
  import { untrack } from 'svelte';
  import type { DesktopIcon } from '$lib/protocol/generated';
  import DesktopIconItem from './DesktopIconItem.svelte';
  import {
    applyClick, applyMarquee, cellBox, cellFromPoint, cellKey, emptySelection, marqueeHits, neighbour, planMove, readingOrder, rectFromPoints,
    resolvePlacements, selectOnly, clamp, capturePointer, type Cell, type Direction, type GridMetrics, type Point, type Selection, type Box,
  } from './layout';
  import { t } from './strings';

  let { icons, metrics, onopen, onmove, oniconcontext, onbackgroundcontext }: {
    icons: DesktopIcon[];
    metrics: GridMetrics;
    onopen: (ids: string[]) => void;
    /** Persist new cells; rejects when the host refused (the layer then snaps back). */
    onmove: (moves: Array<{ id: string; x: number; y: number }>) => Promise<void>;
    oniconcontext: (id: string, at: Point) => void;
    onbackgroundcontext: (at: Point) => void;
  } = $props();

  const DRAG_THRESHOLD = 4;

  // ---- placement (host cells + optimistic overrides -> unique visible cells)
  let overrides = $state<Record<string, Cell>>({});
  const placements = $derived(resolvePlacements(metrics, icons.map((i) => (overrides[i.id] ? { id: i.id, ...overrides[i.id] } : i))));
  const byId = $derived(new Map(icons.map((i) => [i.id, i])));
  const order = $derived(readingOrder(placements));

  // forget overrides the host has caught up with, or whose icon disappeared
  $effect(() => {
    const host = new Map(icons.map((i) => [i.id, i]));
    untrack(() => {
      const next: Record<string, Cell> = {};
      let changed = false;
      for (const [id, c] of Object.entries(overrides)) {
        const h = host.get(id);
        if (h && (h.x !== c.x || h.y !== c.y)) next[id] = c; else changed = true;
      }
      if (changed) overrides = next;
    });
  });

  // ---- selection + focus
  let sel = $state<Selection>(emptySelection());
  let focusedId = $state<string | null>(null);
  $effect(() => {
    const live = new Set(icons.map((i) => i.id));
    untrack(() => {
      if ([...sel.ids].some((id) => !live.has(id))) sel = { ids: new Set([...sel.ids].filter((id) => live.has(id))), anchor: sel.anchor && live.has(sel.anchor) ? sel.anchor : null };
      if (focusedId && !live.has(focusedId)) focusedId = null;
    });
  });
  const tabbableId = $derived(focusedId ?? sel.anchor ?? order[0] ?? null);

  // ---- gestures
  type Gesture =
    | { kind: 'none' }
    | { kind: 'press'; id: string; start: Point; pointerId: number; plainOnSelected: boolean }
    | { kind: 'drag'; id: string; start: Point; pointerId: number; moving: string[]; dx: number; dy: number; plan: Map<string, Cell> }
    | { kind: 'marquee'; pointerId: number; start: Point; current: Point; base: Selection; additive: boolean };
  let gesture = $state<Gesture>({ kind: 'none' });
  let root: HTMLDivElement;
  /** Icon of the latest pointerdown: with pointer capture the dblclick event targets the layer, not the icon. */
  let lastPressId: string | null = null;

  const pointOf = (e: PointerEvent | MouseEvent): Point => {
    const r = root.getBoundingClientRect();
    return { x: e.clientX - r.left, y: e.clientY - r.top };
  };
  const iconIdOf = (target: EventTarget | null): string | null => ((target as HTMLElement | null)?.closest?.('[data-icon-id]') as HTMLElement | null)?.dataset.iconId ?? null;

  function onpointerdown(e: PointerEvent) {
    if (e.button !== 0) return; // right button is handled by oncontextmenu
    const id = iconIdOf(e.target);
    const mods = { ctrl: e.ctrlKey || e.metaKey, shift: e.shiftKey };
    if (id) {
      focusedId = id;
      const plainOnSelected = !mods.ctrl && !mods.shift && sel.ids.has(id) && sel.ids.size > 1;
      if (!plainOnSelected) sel = applyClick(order, sel, id, mods);
      gesture = { kind: 'press', id, start: pointOf(e), pointerId: e.pointerId, plainOnSelected };
      // Capture right away (a flick may leave the icon before the first move event); dblclick is then resolved from lastPressId.
      lastPressId = id;
      capturePointer(root, e.pointerId);
      return;
    }
    // empty desktop: start a rubber band (a plain click on empty space clears the selection)
    lastPressId = null;
    const additive = mods.ctrl || mods.shift;
    const base = additive ? sel : emptySelection();
    if (!additive) sel = base;
    focusedId = null;
    root.focus({ preventScroll: true });
    capturePointer(root, e.pointerId);
    const p = pointOf(e);
    gesture = { kind: 'marquee', pointerId: e.pointerId, start: p, current: p, base, additive };
  }

  function onpointermove(e: PointerEvent) {
    const g = gesture;
    if (g.kind === 'none' || e.pointerId !== g.pointerId) return;
    const p = pointOf(e);
    if (g.kind === 'press') {
      if (Math.hypot(p.x - g.start.x, p.y - g.start.y) < DRAG_THRESHOLD) return;
      const moving = sel.ids.has(g.id) ? order.filter((id) => sel.ids.has(id)) : [g.id];
      gesture = { kind: 'drag', id: g.id, start: g.start, pointerId: g.pointerId, moving, dx: 0, dy: 0, plan: new Map() };
      updateDrag(p);
    } else if (g.kind === 'drag') {
      updateDrag(p);
    } else if (g.kind === 'marquee') {
      // the layer fills the viewport: keep the band inside it
      const cur: Point = { x: clamp(p.x, 0, window.innerWidth), y: clamp(p.y, 0, window.innerHeight) };
      const band = rectFromPoints(g.start, cur);
      sel = applyMarquee(g.base, marqueeHits(placements, metrics, band), g.additive);
      gesture = { ...g, current: cur };
    }
  }

  function updateDrag(p: Point) {
    const g = gesture;
    if (g.kind !== 'drag') return;
    const anchor = placements.get(g.id)!;
    const box = cellBox(metrics, anchor);
    const dx = p.x - g.start.x, dy = p.y - g.start.y;
    const target = cellFromPoint(metrics, { x: box.x + box.width / 2 + dx, y: box.y + box.height / 2 + dy });
    gesture = { ...g, dx, dy, plan: planMove(metrics, placements, g.moving, g.id, target) };
  }

  function finish(e: PointerEvent, cancelled: boolean) {
    const g = gesture;
    if (g.kind === 'none' || e.pointerId !== g.pointerId) return;
    gesture = { kind: 'none' };
    if (root.hasPointerCapture?.(e.pointerId)) root.releasePointerCapture(e.pointerId);
    if (g.kind === 'press') {
      if (!cancelled && g.plainOnSelected) sel = selectOnly(g.id); // click on a member of a multi-selection without dragging
    } else if (g.kind === 'drag' && !cancelled) {
      const moves = [...g.plan].filter(([id, c]) => { const cur = placements.get(id)!; return cur.x !== c.x || cur.y !== c.y; }).map(([id, c]) => ({ id, x: c.x, y: c.y }));
      if (!moves.length) return;
      const before = overrides;
      overrides = { ...overrides, ...Object.fromEntries(moves.map((m) => [m.id, { x: m.x, y: m.y }])) };
      onmove(moves).then(
        () => setTimeout(() => { overrides = Object.fromEntries(Object.entries(overrides).filter(([id]) => !moves.some((m) => m.id === id))); }, 800),
        () => { overrides = before; },
      );
    }
  }

  // ---- context menu / open
  function oncontextmenu(e: MouseEvent) {
    e.preventDefault();
    e.stopPropagation();
    const id = iconIdOf(e.target);
    const at = pointOf(e);
    if (id) {
      if (!sel.ids.has(id)) sel = selectOnly(id);
      focusedId = id;
      oniconcontext(id, at);
    } else {
      sel = emptySelection();
      onbackgroundcontext(at);
    }
  }

  function ondblclick(e: MouseEvent) {
    const id = iconIdOf(e.target) ?? lastPressId;
    if (id && byId.has(id)) onopen([id]);
  }

  // ---- keyboard
  const ARROWS: Record<string, Direction> = { ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down' };

  function focusIcon(id: string) {
    focusedId = id;
    [...root.querySelectorAll<HTMLElement>('[data-icon-id]')].find((el) => el.dataset.iconId === id)?.focus({ preventScroll: true });
  }

  function onkeydown(e: KeyboardEvent) {
    const cur = focusedId ?? tabbableId;
    if (e.key in ARROWS) {
      e.preventDefault();
      if (!cur) { if (order[0]) { sel = selectOnly(order[0]); focusIcon(order[0]); } return; }
      const next = neighbour(placements, cur, ARROWS[e.key]);
      if (!next) return;
      sel = e.shiftKey ? { ids: new Set([...sel.ids, cur, next]), anchor: sel.anchor ?? cur } : selectOnly(next);
      focusIcon(next);
    } else if (e.key === 'Home' || e.key === 'End') {
      e.preventDefault();
      const id = e.key === 'Home' ? order[0] : order[order.length - 1];
      if (id) { sel = selectOnly(id); focusIcon(id); }
    } else if (e.key === 'Enter') {
      e.preventDefault();
      const ids = sel.ids.size ? order.filter((id) => sel.ids.has(id)) : cur ? [cur] : [];
      if (ids.length) onopen(ids.slice(0, 10));
    } else if (e.key === 'a' && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      sel = { ids: new Set(order), anchor: order[0] ?? null };
    } else if (e.key === 'Escape') {
      sel = emptySelection();
    } else if (e.key === ' ' && cur) {
      e.preventDefault();
      sel = applyClick(order, sel, cur, { ctrl: true, shift: false });
    }
  }

  const marquee = $derived<Box | null>(gesture.kind === 'marquee' ? rectFromPoints(gesture.start, gesture.current) : null);
  const dragging = $derived(gesture.kind === 'drag' ? gesture : null);
</script>

<!-- svelte-ignore a11y_no_noninteractive_tabindex -->
<div
  class="layer" bind:this={root} role="listbox" aria-multiselectable="true" aria-label={t('desk.icons')} tabindex="0"
  {onpointerdown} {onpointermove} onpointerup={(e) => finish(e, false)} onpointercancel={(e) => finish(e, true)}
  {oncontextmenu} {ondblclick} {onkeydown}
  onfocusin={(e) => { const id = iconIdOf(e.target); if (id) focusedId = id; }}
>
  {#each icons as icon (icon.id)}
    {@const cell = placements.get(icon.id)}
    {#if cell}
      {@const box = cellBox(metrics, cell)}
      {@const isDragged = dragging?.moving.includes(icon.id) ?? false}
      <div class="cell" style:left="{box.x}px" style:top="{box.y}px" style:width="{box.width}px" style:height="{box.height}px" class:lifted={isDragged}>
        <DesktopIconItem
          {icon} size={metrics.iconSize} selected={sel.ids.has(icon.id)} tabbable={tabbableId === icon.id}
          dragging={isDragged} offset={isDragged && dragging ? { x: dragging.dx, y: dragging.dy } : null}
        />
      </div>
    {/if}
  {/each}

  {#if dragging}
    {#each [...dragging.plan] as [id, cell] (id)}
      {@const box = cellBox(metrics, cell)}
      <div class="ghost" style:left="{box.x + 4}px" style:top="{box.y + 2}px" style:width="{box.width - 8}px" style:height="{box.height - 6}px"></div>
    {/each}
  {/if}

  {#if marquee && (marquee.width > 1 || marquee.height > 1)}
    <div class="marquee" style:left="{marquee.x}px" style:top="{marquee.y}px" style:width="{marquee.width}px" style:height="{marquee.height}px"></div>
  {/if}
</div>

<style>
  .layer { position: absolute; inset: 0; outline: none; touch-action: none; }
  .cell { position: absolute; }
  .cell.lifted { z-index: 5; }
  .ghost {
    position: absolute; border-radius: 10px; pointer-events: none;
    background: rgba(255, 255, 255, 0.14); border: 1px dashed rgba(255, 255, 255, 0.55);
  }
  .marquee {
    position: absolute; pointer-events: none; z-index: 6; border-radius: 2px;
    background: color-mix(in oklab, var(--accent) 22%, transparent);
    border: 1px solid color-mix(in oklab, var(--accent) 75%, white);
  }
</style>
