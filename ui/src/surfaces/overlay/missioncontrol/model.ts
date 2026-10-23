/** Pure Mission Control state helpers: snapshot ordering by rev, desktop selection, filtering and desktop previews. */
import type { DesktopInfo, McSnapshot, McWindow } from '$lib/protocol/generated';

/**
 * Accepts a snapshot unless it is older than the one applied. The host numbers snapshots per overlay show (`rev`), and
 * a mc.changed can overtake the mc.snapshot reply, so a lower rev must never roll the grid back.
 */
export function applySnapshot(current: McSnapshot | null, next: McSnapshot): McSnapshot {
  if (current && next.rev < current.rev) return current;
  return next;
}

/** Keeps the selected desktop when it still exists, otherwise the current one, otherwise the first. */
export function pickSelectedDesktop(desktops: readonly DesktopInfo[], previous: string | null): string | null {
  if (desktops.length === 0) return null;
  if (previous !== null && desktops.some((d) => d.id === previous)) return previous;
  return (desktops.find((d) => d.current) ?? desktops[0]!).id;
}

/**
 * Windows shown in the grid. When desktops are not supported every window is shown. A window with no desktop id is shown
 * on every selection (the host could not place it); otherwise only the selected desktop's windows appear.
 */
export function visibleWindows(snap: McSnapshot, selectedDesktopId: string | null): McWindow[] {
  if (!snap.desktopsSupported || selectedDesktopId === null) return snap.windows;
  return snap.windows.filter((w) => w.desktopId === undefined || w.desktopId === selectedDesktopId);
}

export function windowCountOn(snap: McSnapshot, desktopId: string): number {
  return snap.windows.filter((w) => w.desktopId === desktopId).length;
}

export interface PreviewRect {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * Window rectangles of one desktop as fractions (0..1) of the monitor, for the mini desktop preview. Minimised windows
 * are left out, like Mission Control does. Rects are clamped to the monitor so an off-screen window cannot stretch the
 * preview.
 */
export function previewRects(windows: readonly McWindow[], monitorDip: { w: number; h: number }): PreviewRect[] {
  const out: PreviewRect[] = [];
  if (!(monitorDip.w > 0 && monitorDip.h > 0)) return out;
  for (const w of windows) {
    if (w.minimized) continue;
    const x0 = clamp01(w.bounds.x / monitorDip.w);
    const y0 = clamp01(w.bounds.y / monitorDip.h);
    const x1 = clamp01((w.bounds.x + w.bounds.width) / monitorDip.w);
    const y1 = clamp01((w.bounds.y + w.bounds.height) / monitorDip.h);
    if (x1 - x0 <= 0 || y1 - y0 <= 0) continue;
    out.push({ x: x0, y: y0, w: x1 - x0, h: y1 - y0 });
  }
  return out;
}

/** Optimistic local removal after mc.closeWindow (the next snapshot or mc.changed confirms it). */
export function withoutWindow(snap: McSnapshot, handle: string): McSnapshot {
  return { ...snap, windows: snap.windows.filter((w) => w.handle !== handle) };
}

/** Optimistic local move after mc.moveWindow succeeded. */
export function withWindowOnDesktop(snap: McSnapshot, handle: string, desktopId: string): McSnapshot {
  return { ...snap, windows: snap.windows.map((w) => (w.handle === handle ? { ...w, desktopId } : w)) };
}

/** A stable hue (0..359) for a key, used for fallback tiles and desktop wallpapers. */
export function hueFor(key: string): number {
  let h = 5381;
  for (let i = 0; i < key.length; i++) h = ((h << 5) + h + key.charCodeAt(i)) | 0;
  return Math.abs(h) % 360;
}

function clamp01(v: number): number {
  return Number.isFinite(v) ? Math.min(1, Math.max(0, v)) : 0;
}
