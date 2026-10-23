import { getBridge } from './ipc';
import type { ShapeRect } from './protocol/generated';

/**
 * Glass-panel reporting. Mark every translucent panel with `use:glassShape` (optionally { radius });
 * the union of all marked elements is sent to the host as `surface.shape` so it can apply DWM blur-behind to exactly
 * those rounded rects and make everything else click-through (non-glass interactive overflow goes through `use:hitShape`). Coordinates are DIPs relative to the viewport.
 */
const elements = new Map<HTMLElement, { radius?: number }>();
const hitElements = new Map<HTMLElement, { radius?: number }>();
let scheduled = false;
let lastJson = '';
// ONE shared poll timer for all registered elements (transforms such as dock magnification do not trigger ResizeObserver). Runs only while something is registered.
let pollTimer: ReturnType<typeof setInterval> | null = null;
function ensureTimer() { if (pollTimer === null) pollTimer = setInterval(scheduleShapeReport, 250); }
function maybeStopTimer() { if (elements.size + hitElements.size === 0 && pollTimer !== null) { clearInterval(pollTimer); pollTimer = null; } }

function radiusOf(el: HTMLElement, o: { radius?: number }): number {
  if (o.radius !== undefined) return o.radius;
  const r = parseFloat(getComputedStyle(el).borderTopLeftRadius);
  return Number.isFinite(r) ? r : 0;
}

export function collectShapes(source: Map<HTMLElement, { radius?: number }> = elements): ShapeRect[] {
  const out: ShapeRect[] = [];
  for (const [el, o] of source) {
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
    const rects = collectShapes(elements);
    const hitRects = collectShapes(hitElements);
    const json = JSON.stringify([rects, hitRects]);
    if (json === lastJson) return;
    lastJson = json;
    getBridge().emit('surface.shape', { rects, hitRects, blur: true });
  });
}

/** Svelte action. */
export function glassShape(node: HTMLElement, opts: { radius?: number } = {}) {
  elements.set(node, opts);
  const ro = new ResizeObserver(scheduleShapeReport);
  ro.observe(node);
  ensureTimer();
  scheduleShapeReport();
  return {
    update(o: { radius?: number }) { elements.set(node, o ?? {}); scheduleShapeReport(); },
    destroy() { elements.delete(node); ro.disconnect(); maybeStopTimer(); scheduleShapeReport(); },
  };
}

/**
 * Svelte action for content that is VISIBLE AND INTERACTIVE but not glass (magnified dock icons overflowing the panel, tooltips).
 * The host includes these rects in the click region but not in the blurred backdrop (when SurfaceInit.glassOverflow is true).
 * If glassOverflow is false the UI must keep such content inside a glass panel instead.
 */
export function hitShape(node: HTMLElement, opts: { radius?: number } = {}) {
  hitElements.set(node, opts);
  const ro = new ResizeObserver(scheduleShapeReport);
  ro.observe(node);
  ensureTimer();
  scheduleShapeReport();
  return {
    update(o: { radius?: number }) { hitElements.set(node, o ?? {}); scheduleShapeReport(); },
    destroy() { hitElements.delete(node); ro.disconnect(); maybeStopTimer(); scheduleShapeReport(); },
  };
}
