import type { AudioState, BluetoothState, MediaState, NetworkState, Settings, TrayIcon, TrayState } from '$lib/protocol/generated';
import { batteryVisual } from './battery';

/** Every item that can open an overlay panel from the bar. Also the pressed-state keys (see popoverTracker.ts). */
export type ItemKey =
  | 'apple' | 'appMenu' | 'spotlight' | 'controlCenter' | 'calendar'
  | 'battery' | 'network' | 'sound' | 'bluetooth' | 'media';

/** State mirrors; `null` = that state event has not arrived yet (the item stays hidden instead of flashing a wrong default). */
export interface BarInputs {
  battery: import('$lib/protocol/generated').BatteryState | null;
  network: NetworkState | null;
  audio: AudioState | null;
  bluetooth: BluetoothState | null;
  media: MediaState | null;
  tray: TrayState | null;
}

/** Items of the right-hand cluster, left to right, followed by the clock which is always last. */
export type RightItem = 'tray' | ItemKey;

/**
 * Right cluster order, left to right (macOS convention: the clock sits at the far right, Control Center and Spotlight next to it,
 * the status glyphs further left). Read right to left this is: clock, Control Center, Spotlight, battery, sound, Wi-Fi,
 * Bluetooth, now playing, tray.
 */
export const RIGHT_ORDER: readonly RightItem[] = ['tray', 'media', 'bluetooth', 'network', 'sound', 'battery', 'spotlight', 'controlCenter', 'calendar'];

export function mediaVisible(m: MediaState | null): boolean {
  return !!m && m.available && !!m.title?.trim();
}

export function bluetoothVisible(b: BluetoothState | null): boolean {
  return !!b && b.supported;
}

export function soundVisible(a: AudioState | null): boolean {
  // No output device at all (before the first enumeration, or a machine without audio hardware): nothing to control.
  return !!a && (a.devices.length > 0 || !!a.deviceId);
}

/** Tray icons that are drawn: only when the host can host a tray, and never the ones the user parked in overflow. */
export function visibleTrayIcons(t: TrayState | null): TrayIcon[] {
  return t && t.supported ? t.icons.filter((i) => !i.hidden) : [];
}

/** Which items are rendered, in order. */
export function visibleItems(i: BarInputs): RightItem[] {
  const shown: Record<RightItem, boolean> = {
    tray: visibleTrayIcons(i.tray).length > 0,
    media: mediaVisible(i.media),
    bluetooth: bluetoothVisible(i.bluetooth),
    network: !!i.network,
    sound: soundVisible(i.audio),
    battery: batteryVisual(i.battery).visible,
    spotlight: true,
    controlCenter: true,
    calendar: true,
    // keys below never appear in the right cluster
    apple: false,
    appMenu: false,
  };
  return RIGHT_ORDER.filter((k) => shown[k]);
}

export type SoundLevel = 'muted' | 'zero' | 'low' | 'mid' | 'high';

/** Number of sound waves drawn: muted gets a slash, zero volume a bare speaker. */
export function soundLevel(a: Pick<AudioState, 'volume' | 'muted'>): SoundLevel {
  if (a.muted) return 'muted';
  const v = Number.isFinite(a.volume) ? a.volume : 0;
  if (v <= 0.004) return 'zero';
  if (v < 0.34) return 'low';
  if (v < 0.67) return 'mid';
  return 'high';
}

export const soundPercent = (a: Pick<AudioState, 'volume'>): number => Math.round(Math.min(1, Math.max(0, a.volume || 0)) * 100);

export type NetworkGlyph = 'wifi' | 'ethernet' | 'cellular' | 'other' | 'none';

export interface NetworkVisual {
  glyph: NetworkGlyph;
  /** Lit Wi-Fi arcs, 0..3 (4 signal bars map onto 3 arcs). Only meaningful for 'wifi'. */
  arcs: 0 | 1 | 2 | 3;
  connected: boolean;
}

export function networkVisual(n: NetworkState): NetworkVisual {
  if (!n.connected || n.kind === 'none') return { glyph: 'none', arcs: 0, connected: false };
  if (n.kind === 'wifi') {
    const bars = Math.min(4, Math.max(0, Math.round(Number.isFinite(n.signalBars) ? n.signalBars : 0)));
    return { glyph: 'wifi', arcs: Math.round(bars * 0.75) as 0 | 1 | 2 | 3, connected: true };
  }
  return { glyph: n.kind, arcs: 0, connected: true };
}

export function connectedBluetoothCount(b: BluetoothState): number {
  return b.devices.filter((d) => d.connected).length;
}

/** Normalised view of the settings the bar reads (guards against partially filled settings from an older file). */
export function barSettings(s: Settings) {
  const m = s.menuBar;
  return {
    translucent: m.translucent !== false,
    showBatteryPercent: m.showBatteryPercent !== false,
    clock: { use24Hour: m.use24Hour !== false, showSeconds: m.showSeconds === true, showDate: m.showDate !== false },
  };
}
