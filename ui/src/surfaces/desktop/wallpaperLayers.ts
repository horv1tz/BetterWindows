// Wallpaper crossfade as a tiny state machine: a new URL becomes a new top layer that fades in once the image has decoded; layers below
// it are dropped when it is fully visible. A failed image never replaces what is on screen.
import type { WallpaperFit } from '$lib/protocol/generated';

export interface WpLayer { id: number; url: string; ready: boolean }

/** Add a layer for `url` (no-op when the newest layer already shows it). */
export function pushLayer(layers: readonly WpLayer[], url: string, id: number): WpLayer[] {
  if (layers.length && layers[layers.length - 1].url === url) return [...layers];
  return [...layers, { id, url, ready: false }];
}

export function markReady(layers: readonly WpLayer[], id: number): WpLayer[] {
  return layers.map((l) => (l.id === id ? { ...l, ready: true } : l));
}

/** Image failed to load/decode: forget that layer. */
export function markFailed(layers: readonly WpLayer[], id: number): WpLayer[] {
  return layers.filter((l) => l.id !== id);
}

/** After the top layer finished fading in, everything beneath it is invisible and can go. */
export function settle(layers: readonly WpLayer[], topId: number): WpLayer[] {
  const idx = layers.findIndex((l) => l.id === topId);
  return idx > 0 && layers[idx].ready ? layers.slice(idx) : [...layers];
}

export interface FitStyle { objectFit: 'cover' | 'contain' | 'fill' | 'none'; tile: boolean }

/** CSS for DesktopState.wallpaperFit. */
export function fitStyle(fit: WallpaperFit): FitStyle {
  switch (fit) {
    case 'fit': return { objectFit: 'contain', tile: false };
    case 'stretch': return { objectFit: 'fill', tile: false };
    case 'center': return { objectFit: 'none', tile: false };
    case 'tile': return { objectFit: 'none', tile: true };
    default: return { objectFit: 'cover', tile: false };
  }
}
