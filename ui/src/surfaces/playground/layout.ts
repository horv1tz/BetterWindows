// Pure layout math of the playground: where each surface iframe goes, how faux windows are clamped and stacked.
import type { DockPosition } from '$lib/protocol/generated';

export interface Size { width: number; height: number }
export interface Frame { left: number; top: number; width: number; height: number }

export const MENUBAR = 28;
export const MONITOR_GAP = 2;

/** One frame per monitor, side by side, filling the viewport (a 2 px bezel between them). */
export function screenFrames(viewport: Size, count: number, gap = MONITOR_GAP): Frame[] {
  const n = Math.max(1, Math.min(4, Math.floor(count) || 1));
  const w = (viewport.width - gap * (n - 1)) / n;
  return Array.from({ length: n }, (_, i) => ({ left: Math.round(i * (w + gap)), top: 0, width: Math.round(w), height: viewport.height }));
}

/** Monitor ids are "1", "2", ...; anything else (or no id) means the primary monitor. */
export function monitorIndex(id: string | undefined | null, count: number): number {
  const n = Number(id);
  return Number.isInteger(n) && n >= 1 && n <= count ? n - 1 : 0;
}

/**
 * The dock window: the UI decides its size (surface.resize); the host keeps it centred on its screen edge.
 * Returned relative to the screen it lives on. The menu bar's strip is never covered for left/right docks.
 */
export function dockFrame(screen: Size, dock: Size, position: DockPosition, menubar = MENUBAR): Frame {
  const width = Math.min(Math.max(0, dock.width), screen.width);
  const height = Math.min(Math.max(0, dock.height), screen.height - menubar);
  if (position === 'left') return { left: 0, top: Math.round(menubar + (screen.height - menubar - height) / 2), width, height };
  if (position === 'right') return { left: screen.width - width, top: Math.round(menubar + (screen.height - menubar - height) / 2), width, height };
  return { left: Math.round((screen.width - width) / 2), top: screen.height - height, width, height };
}

/** Keep a faux window reachable: its title bar stays below the menu bar and at least `keep` px of it stay on the screen horizontally. */
export function clampWindow(f: Frame, bounds: Size, minTop = MENUBAR, keep = 96): Frame {
  const top = Math.min(Math.max(f.top, minTop), Math.max(minTop, bounds.height - 40));
  const left = Math.min(Math.max(f.left, keep - f.width), bounds.width - keep);
  return { ...f, left, top };
}

export type ZOrder = Record<string, number>;

/** Raise a window above all others (the others keep their relative order). */
export function raise(z: ZOrder, id: string): ZOrder {
  const top = Math.max(0, ...Object.values(z));
  if (z[id] === top && Object.values(z).filter((v) => v === top).length === 1) return z;
  return { ...z, [id]: top + 1 };
}

export const topWindow = (z: ZOrder): string | null => Object.entries(z).sort(([, a], [, b]) => b - a)[0]?.[0] ?? null;

export type FauxKind = 'browser' | 'terminal' | 'notepad';
export interface FauxSpec { id: string; kind: FauxKind; appName: string; titleKey: string; frame: Frame }

/** Initial faux windows of the primary screen, placed so the desktop's widgets (right) and icons (left) stay mostly visible. */
export function defaultWindows(screen: Size): FauxSpec[] {
  const px = (frac: number) => Math.round(screen.width * frac);
  const py = (frac: number) => Math.round(screen.height * frac);
  const clampW = (v: number, min: number, max: number) => Math.round(Math.min(max, Math.max(min, v)));
  const right = px(0.56); // the widget columns start at about 57% of the width
  const browser = { left: px(0.17), width: clampW(right - px(0.17), 360, 640), height: clampW(py(0.5), 280, 460) };
  const terminal = { left: px(0.25), width: clampW(right - px(0.25), 340, 520), height: clampW(py(0.34), 220, 320) };
  const notepad = { left: px(0.2), width: clampW(px(0.25), 300, 420), height: clampW(py(0.28), 200, 280) };
  return [
    { id: 'win-browser', kind: 'browser', appName: 'Microsoft Edge', titleKey: 'pg.win.browser', frame: { top: py(0.09), ...browser } },
    { id: 'win-notepad', kind: 'notepad', appName: 'Notepad', titleKey: 'pg.win.notepad', frame: { top: py(0.54), ...notepad } },
    { id: 'win-terminal', kind: 'terminal', appName: 'Terminal', titleKey: 'pg.win.terminal', frame: { top: py(0.31), ...terminal } },
  ];
}
