<script lang="ts">
  // The card around one widget: glass surface, drag to move (8 DIP snap, kept inside the monitor), keyboard move, hover lift,
  // right-click menu hook. The content component is chosen by the registry.
  import { onDestroy } from 'svelte';
  import type { WidgetInstance, WidgetSize } from '$lib/protocol/generated';
  import { clamp, clampWidget, snapWidget, widgetBox, WIDGET_SNAP, type Insets, type Point, type Size } from './layout';
  import { createDebouncer } from './debounce';
  import { WIDGETS } from './widgets/registry';
  import { t } from './strings';

  let { widget, viewport, insets, onmove, onmenu, onconfig }: {
    widget: WidgetInstance;
    viewport: Size;
    insets: Insets;
    onmove: (p: Point) => void;
    onmenu: (at: Point) => void;
    onconfig: (config: Record<string, unknown>) => void;
  } = $props();

  const def = $derived(WIDGETS[widget.type]);
  const size = $derived<WidgetSize>(widget.size);
  const box = $derived(widgetBox(size));
  const config = $derived((widget.config && typeof widget.config === 'object' && !Array.isArray(widget.config) ? widget.config : {}) as Record<string, unknown>);

  // The stored position may lie outside the monitor (smaller resolution, other layout): show it clamped, store it only when the user moves it.
  const home = $derived(clampWidget({ x: widget.x, y: widget.y }, size, viewport, insets));

  let drag = $state<{ pointerId: number; start: Point; origin: Point; pos: Point; active: boolean } | null>(null);
  let nudge = $state<Point | null>(null);
  const commitNudge = createDebouncer<Point>((p) => { onmove(p); }, 350);
  onDestroy(() => commitNudge.flush());

  const pos = $derived(drag?.active ? drag.pos : nudge ?? home);
  // drop the keyboard override once the host reported the same position
  $effect(() => { if (nudge && nudge.x === widget.x && nudge.y === widget.y) nudge = null; });

  const INTERACTIVE = 'button, input, textarea, select, a, [data-nodrag], [contenteditable="true"]';

  function onpointerdown(e: PointerEvent) {
    if (e.button !== 0 || (e.target as HTMLElement).closest(INTERACTIVE)) return;
    drag = { pointerId: e.pointerId, start: { x: e.clientX, y: e.clientY }, origin: pos, pos, active: false };
  }

  function onpointermove(e: PointerEvent) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    const dx = e.clientX - drag.start.x, dy = e.clientY - drag.start.y;
    if (!drag.active) {
      if (Math.hypot(dx, dy) < 4) return;
      (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
      drag.active = true;
    }
    drag.pos = snapWidget({ x: drag.origin.x + dx, y: drag.origin.y + dy }, size, viewport, insets);
  }

  function end(e: PointerEvent, cancel: boolean) {
    if (!drag || e.pointerId !== drag.pointerId) return;
    const d = drag;
    drag = null;
    if ((e.currentTarget as HTMLElement).hasPointerCapture?.(e.pointerId)) (e.currentTarget as HTMLElement).releasePointerCapture(e.pointerId);
    if (d.active && !cancel && (d.pos.x !== home.x || d.pos.y !== home.y)) {
      nudge = d.pos; // keep showing it until desktop.state confirms
      onmove(d.pos);
    }
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.target !== e.currentTarget) return; // keys typed into widget content belong to the content
    const step = e.shiftKey ? WIDGET_SNAP * 4 : WIDGET_SNAP;
    const delta: Record<string, Point> = { ArrowLeft: { x: -step, y: 0 }, ArrowRight: { x: step, y: 0 }, ArrowUp: { x: 0, y: -step }, ArrowDown: { x: 0, y: step } };
    if (e.key in delta) {
      e.preventDefault();
      const d = delta[e.key];
      const base = pos;
      const next = snapWidget({ x: base.x + d.x, y: base.y + d.y }, size, viewport, insets);
      nudge = next;
      commitNudge.push(next);
    } else if (e.key === 'ContextMenu' || (e.key === 'F10' && e.shiftKey)) {
      e.preventDefault();
      const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
      onmenu({ x: r.left + 24, y: r.top + 24 });
    }
  }

  function oncontextmenu(e: MouseEvent) {
    e.preventDefault();
    e.stopPropagation();
    onmenu({ x: e.clientX, y: e.clientY });
  }
</script>

<div
  class="slot" class:dragging={drag?.active}
  style:transform="translate3d({pos.x}px, {pos.y}px, 0)" style:width="{box.width}px" style:height="{box.height}px"
>
  <!-- svelte-ignore a11y_no_noninteractive_tabindex, a11y_no_noninteractive_element_interactions -->
  <div
    class="card glass {widget.type}" class:dragging={drag?.active}
    role="group" aria-roledescription="widget" aria-label={t(`w.name.${widget.type}`)} tabindex="0"
    data-widget-id={widget.id} data-type={widget.type} data-size={size}
    {onpointerdown} {onpointermove} onpointerup={(e) => end(e, false)} onpointercancel={(e) => end(e, true)} {onkeydown} {oncontextmenu}
  >
    {#key size}
      <div class="content">
        {#if def}
          <def.component {widget} {config} {size} saveConfig={onconfig} />
        {/if}
      </div>
    {/key}
  </div>
</div>

<style>
  .slot { position: absolute; left: 0; top: 0; will-change: transform; animation: appear 280ms var(--ease-out) both; }
  .slot.dragging { z-index: 20; }
  .card {
    position: absolute; inset: 0; border-radius: 22px; overflow: hidden; outline: none; touch-action: none;
    transition: transform var(--dur) var(--ease-out), box-shadow var(--dur) var(--ease-out);
  }
  .card:hover { transform: translateY(-2px); box-shadow: var(--shadow-lg); }
  .card.dragging { transform: scale(1.025); box-shadow: var(--shadow-lg); transition: transform var(--dur-fast) var(--ease-out); cursor: grabbing; }
  .card:focus-visible { box-shadow: var(--shadow-md), 0 0 0 2px color-mix(in oklab, var(--accent) 85%, white); }
  .content { position: absolute; inset: 0; animation: fade 160ms ease both; }
  @keyframes appear { from { opacity: 0; scale: 0.96; } to { opacity: 1; scale: 1; } }
  @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
</style>
