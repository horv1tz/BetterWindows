import { getBridge } from './ipc';
import type { ShapeRect } from './protocol/generated';

/**
 * Glass-panel reporting. Mark every translucent panel with `use:glassShape` (optionally { radius });
 * the union of all marked elements is sent to the host as `surface.shape` so it can apply DWM blur-behind to exactly
 * those rounded rects and make everything else click-through. Coordinates are DIPs relative to the viewport.
 */
const elements = new Map<HTMLElement, { radius?: number }>();
let scheduled = false;
let lastJson = '';

function radiusOf(el: HTMLElement, o: { radius?: number }): number {
  if (o.radius !== undefined) return o.radius;
  const r = parseFloat(getComputedStyle(el).borderTopLeftRadius);
  return Number.isFinite(r) ? r : 0;
}

export function collectShapes(): ShapeRect[] {
  const out: ShapeRect[] = [];
  for (const [el, o] of elements) {
    const b = el.getBoundingClientRect();
    if (b.width < 1 || b.height < 1) continue;
    out.push({ x: Math.round(b.x * 10) / 10, y: Math.round(b.y * 10) / 10, width: Math.round(b.width * 10) / 10, height: Math.round(b.height * 10) / 10, radius: radiusOf(el, o) });
  }
  return out;
}

export function scheduleShapeReport() {
  if (scheduled) return;
  scheduled = true;
  requestAnimationFrame(() => {
    scheduled = false;
    const rects = collectShapes();
    const json = JSON.stringify(rects);
    if (json === lastJson) return;
    lastJson = json;
    getBridge().emit('surface.shape', { rects, blur: true });
  });
}

/** Svelte action. */
export function glassShape(node: HTMLElement, opts: { radius?: number } = {}) {
  elements.set(node, opts);
  const ro = new ResizeObserver(scheduleShapeReport);
  ro.observe(node);
  // Transforms (dock magnification) do not trigger ResizeObserver: poll cheaply while connected.
  const timer = setInterval(scheduleShapeReport, 250);
  scheduleShapeReport();
  return {
    update(o: { radius?: number }) { elements.set(node, o ?? {}); scheduleShapeReport(); },
    destroy() { elements.delete(node); ro.disconnect(); clearInterval(timer); scheduleShapeReport(); },
  };
}
