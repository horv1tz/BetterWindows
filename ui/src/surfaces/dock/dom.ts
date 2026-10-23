import { pillThickness, type DockPos, type Metrics, type Slot } from './layout';
import type { Frame } from './motion';

/**
 * Geometry <-> DOM glue for the dock (kept free of Svelte so it can be unit tested with jsdom):
 * orientation-dependent inline styles for slots, the per-frame writer, pointer coordinate conversion and tooltip placement.
 * Everything animated here is transform-only, except the pill background in the "pill grows" mode (see applyFrame).
 */

/** Edge-side transform origin of the magnified part: icons grow away from the docked screen edge. */
export function originOf(pos: DockPos): string {
  return pos === 'bottom' ? '50% 100%' : pos === 'left' ? '0% 50%' : '100% 50%';
}

/** Distance (px) between the pill's outer edge and the near side of a slot on the cross axis. */
export function crossOffset(m: Metrics, s: Slot): number {
  return m.pad + (s.kind === 'sep' ? (m.size - s.cross) / 2 : 0);
}

/** Absolute box of a slot inside the stage (the rest pill box). */
export function slotStyle(pos: DockPos, m: Metrics, s: Slot): string {
  const main = m.pad + s.start;
  const off = crossOffset(m, s);
  if (pos === 'bottom') return `left:${main}px;bottom:${off}px;width:${s.len}px;height:${s.cross}px`;
  if (pos === 'left') return `top:${main}px;left:${off}px;height:${s.len}px;width:${s.cross}px`;
  return `top:${main}px;right:${off}px;height:${s.len}px;width:${s.cross}px`;
}

export function translateOf(pos: DockPos, offset: number): string {
  if (Math.abs(offset) < 0.02) return '';
  const o = Math.round(offset * 20) / 20;
  return pos === 'bottom' ? `translate3d(${o}px,0,0)` : `translate3d(0,${o}px,0)`;
}

export function scaleOf(pos: DockPos, kind: Slot['kind'], k: number): string {
  if (Math.abs(k - 1) < 0.0008) return '';
  const v = Math.round(k * 2000) / 2000;
  // a separator line only gets longer, never thicker
  if (kind === 'sep') return pos === 'bottom' ? `scaleY(${v})` : `scaleX(${v})`;
  return `scale(${v})`;
}

export interface SlotRefs { slot: HTMLElement; mag: HTMLElement | null }

const lastWritten = new WeakMap<HTMLElement, string>();
function write(el: HTMLElement, prop: 'transform', value: string) {
  if (lastWritten.get(el) === value) return;
  lastWritten.set(el, value);
  el.style[prop] = value;
}

/**
 * Writes one animation frame to the DOM: two transforms per slot and the pill rectangle.
 * In the "pill grows" mode (host cannot blur outside the pill) the pill's own box changes size every frame, because the window region
 * the host derives from it must follow the icons; that is a single absolutely positioned element, so layout cost is negligible.
 */
export function applyFrame(f: Frame, slots: Slot[], refs: Map<string, SlotRefs>, pill: HTMLElement | null, pos: DockPos, m: Metrics, pillGrows: boolean): void {
  for (let i = 0; i < slots.length; i++) {
    const r = refs.get(slots[i].key);
    const fs = f.slots[i];
    if (!r || !fs) continue;
    write(r.slot, 'transform', translateOf(pos, fs.offset));
    if (r.mag) write(r.mag, 'transform', scaleOf(pos, slots[i].kind, fs.scale));
  }
  if (!pill) return;
  const T = pillThickness(m);
  const grow = pillGrows ? Math.round(f.grow * 10) / 10 : 0;
  const start = Math.round((m.pad + f.pillStart) * 10) / 10;
  const len = Math.round((f.pillEnd - f.pillStart) * 10) / 10;
  // single-property writes (not cssText): the border-radius set by the component must survive
  const box = pos === 'bottom'
    ? { left: start, width: len, top: -grow, height: T + grow }
    : { left: pos === 'left' ? 0 : -grow, width: T + grow, top: start, height: len };
  const key = `${box.left}|${box.width}|${box.top}|${box.height}`;
  if (lastWritten.get(pill) === key) return;
  lastWritten.set(pill, key);
  pill.style.left = `${box.left}px`;
  pill.style.width = `${box.width}px`;
  pill.style.top = `${box.top}px`;
  pill.style.height = `${box.height}px`;
}

export interface RectLike { left: number; top: number; right: number; bottom: number }

/** Pointer position on the main axis in strip coordinates (0 = start of the first slot). */
export function mainOf(pos: DockPos, stage: RectLike, m: Metrics, clientX: number, clientY: number): number {
  return (pos === 'bottom' ? clientX - stage.left : clientY - stage.top) - m.pad;
}

/** Pointer distance from the pill's outer (screen-edge) side, positive towards the screen centre. */
export function crossOf(pos: DockPos, stage: RectLike, clientX: number, clientY: number): number {
  return pos === 'bottom' ? stage.bottom - clientY : pos === 'left' ? clientX - stage.left : stage.right - clientX;
}

/** How far the pointer is beyond the REST pill on the cross axis (<= 0 inside). */
export function outsideOf(pos: DockPos, stage: RectLike, m: Metrics, clientX: number, clientY: number): number {
  return crossOf(pos, stage, clientX, clientY) - pillThickness(m);
}

export interface TipInput {
  pos: DockPos;
  m: Metrics;
  slot: Slot;
  /** Hovered slot's frame data. */
  offset: number;
  scale: number;
  grow: number;
  pillGrows: boolean;
  tipW: number;
  tipH: number;
  /** Allowed range of the tooltip's main-axis position in stage coordinates (keeps it inside the window). */
  minMain: number;
  maxMain: number;
  gap?: number;
}

/** Top-left of the tooltip bubble in stage coordinates: centred over the hovered icon, floating just beyond its (magnified) top. */
export function tooltipPlacement(i: TipInput): { x: number; y: number } {
  const { m, pos } = i;
  const gap = i.gap ?? 8;
  const T = pillThickness(m);
  const centre = m.pad + i.slot.start + i.slot.len / 2 + i.offset;
  const extent = i.pillGrows ? T + i.grow : Math.max(T, m.pad + i.scale * i.slot.cross);
  const along = (size: number) => {
    const hi = Math.max(i.minMain, i.maxMain - size);
    return Math.min(Math.max(centre - size / 2, i.minMain), hi);
  };
  if (pos === 'bottom') return { x: along(i.tipW), y: T - extent - gap - i.tipH };
  if (pos === 'left') return { x: extent + gap, y: along(i.tipH) };
  return { x: T - extent - gap - i.tipW, y: along(i.tipH) };
}

export function isTouch(e: { pointerType?: string }): boolean { return e.pointerType === 'touch'; }

/** Client coordinates of a slot's REST centre (+ an optional main-axis shift), given the rest pill rectangle. */
export function slotCenterClient(pos: DockPos, stage: RectLike, m: Metrics, s: Slot, shift = 0): { x: number; y: number } {
  const main = m.pad + s.start + s.len / 2 + shift;
  const off = crossOffset(m, s) + s.cross / 2;
  if (pos === 'bottom') return { x: stage.left + main, y: stage.bottom - off };
  if (pos === 'left') return { x: stage.left + off, y: stage.top + main };
  return { x: stage.right - off, y: stage.top + main };
}

/** The rectangle a popover should hang off: the slot's (magnified) extent on the main axis, the whole rest pill on the cross axis. */
export function popoverAnchor(pos: DockPos, stage: RectLike, m: Metrics, s: Slot, offset: number, scale: number): { x: number; y: number; width: number; height: number } {
  const T = pillThickness(m);
  const len = s.len * scale;
  const c = m.pad + s.start + s.len / 2 + offset;
  if (pos === 'bottom') return { x: stage.left + c - len / 2, y: stage.bottom - T, width: len, height: T };
  if (pos === 'left') return { x: stage.left, y: stage.top + c - len / 2, width: T, height: len };
  return { x: stage.right - T, y: stage.top + c - len / 2, width: T, height: len };
}
