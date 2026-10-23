import { describe, expect, it } from 'vitest';
import type { AudioState, BluetoothState, MediaState, NetworkState, Settings, TrayState } from '$lib/protocol/generated';
import { defaultSettings } from '$lib/protocol/generated';
import {
  barSettings, connectedBluetoothCount, mediaVisible, networkVisual, soundLevel, soundPercent, visibleItems, visibleTrayIcons,
  type BarInputs,
} from './status';

const audio: AudioState = { volume: 0.5, muted: false, deviceId: 'spk', devices: [{ id: 'spk', name: 'Speakers', isDefault: true }] };
const media = (o: Partial<MediaState> = {}): MediaState => ({
  available: true, title: 'Song', playing: true, positionSec: 0, durationSec: 100, canPlay: true, canPause: true, canNext: true, canPrevious: true, canSeek: true, ...o,
});
const all = (o: Partial<BarInputs> = {}): BarInputs => ({
  battery: { present: true, percent: 80, charging: false, acOnline: false },
  network: { kind: 'wifi', connected: true, signalBars: 3 },
  audio,
  bluetooth: { supported: true, enabled: true, devices: [] },
  media: media(),
  tray: { supported: true, icons: [{ id: 'a', tooltip: 'A', iconUrl: 'x', hidden: false }] },
  ...o,
});

describe('visibleItems', () => {
  it('shows everything left to right, clock last', () => {
    expect(visibleItems(all())).toEqual(['tray', 'media', 'bluetooth', 'network', 'sound', 'battery', 'spotlight', 'controlCenter', 'calendar']);
  });

  it('shows only the always-on items before any state has arrived', () => {
    const empty: BarInputs = { battery: null, network: null, audio: null, bluetooth: null, media: null, tray: null };
    expect(visibleItems(empty)).toEqual(['spotlight', 'controlCenter', 'calendar']);
  });

  it('hides the battery when no battery is present', () => {
    expect(visibleItems(all({ battery: { present: false, percent: 0, charging: false, acOnline: true } }))).not.toContain('battery');
  });

  it('hides the tray when unsupported (the Explorer-alongside default) or when it has no visible icons', () => {
    expect(visibleItems(all({ tray: { supported: false, icons: [{ id: 'a', tooltip: '', iconUrl: '', hidden: false }] } }))).not.toContain('tray');
    expect(visibleItems(all({ tray: { supported: true, icons: [] } }))).not.toContain('tray');
    expect(visibleItems(all({ tray: { supported: true, icons: [{ id: 'a', tooltip: '', iconUrl: '', hidden: true }] } }))).not.toContain('tray');
  });

  it('hides Bluetooth when unsupported but keeps it (dimmed by the item) when merely off', () => {
    expect(visibleItems(all({ bluetooth: { supported: false, enabled: false, devices: [] } }))).not.toContain('bluetooth');
    expect(visibleItems(all({ bluetooth: { supported: true, enabled: false, devices: [] } }))).toContain('bluetooth');
  });

  it('hides sound without any output device', () => {
    expect(visibleItems(all({ audio: { volume: 0, muted: false, devices: [] } }))).not.toContain('sound');
    expect(visibleItems(all({ audio: { ...audio, devices: [] } }))).toContain('sound');
  });

  it('keeps the network item for "no connection" (it shows the offline glyph)', () => {
    expect(visibleItems(all({ network: { kind: 'none', connected: false, signalBars: 0 } }))).toContain('network');
  });
});

describe('mediaVisible', () => {
  it('needs availability and a non-blank title, playing or paused', () => {
    expect(mediaVisible(media())).toBe(true);
    expect(mediaVisible(media({ playing: false }))).toBe(true);
    expect(mediaVisible(media({ available: false }))).toBe(false);
    expect(mediaVisible(media({ title: undefined }))).toBe(false);
    expect(mediaVisible(media({ title: '   ' }))).toBe(false);
    expect(mediaVisible(null)).toBe(false);
  });
});

describe('soundLevel / soundPercent', () => {
  it('maps volume to the number of waves', () => {
    expect(soundLevel({ volume: 0, muted: false })).toBe('zero');
    expect(soundLevel({ volume: 0.2, muted: false })).toBe('low');
    expect(soundLevel({ volume: 0.34, muted: false })).toBe('mid');
    expect(soundLevel({ volume: 0.66, muted: false })).toBe('mid');
    expect(soundLevel({ volume: 0.67, muted: false })).toBe('high');
    expect(soundLevel({ volume: 1, muted: false })).toBe('high');
  });
  it('muted wins over the volume', () => {
    expect(soundLevel({ volume: 0.9, muted: true })).toBe('muted');
  });
  it('tolerates garbage', () => {
    expect(soundLevel({ volume: Number.NaN, muted: false })).toBe('zero');
    expect(soundPercent({ volume: 1.4 })).toBe(100);
    expect(soundPercent({ volume: 0.555 })).toBe(56);
  });
});

describe('networkVisual', () => {
  const n = (o: Partial<NetworkState>): NetworkState => ({ kind: 'wifi', connected: true, signalBars: 4, ...o });
  it('maps 0..4 signal bars to 0..3 lit arcs', () => {
    expect([0, 1, 2, 3, 4].map((signalBars) => networkVisual(n({ signalBars })).arcs)).toEqual([0, 1, 2, 2, 3]);
  });
  it('clamps absurd bar counts', () => {
    expect(networkVisual(n({ signalBars: 9 })).arcs).toBe(3);
    expect(networkVisual(n({ signalBars: -2 })).arcs).toBe(0);
  });
  it('not connected or kind none is the offline glyph', () => {
    expect(networkVisual(n({ connected: false })).glyph).toBe('none');
    expect(networkVisual(n({ kind: 'none' })).glyph).toBe('none');
  });
  it('ethernet / cellular / other keep their glyph', () => {
    expect(networkVisual(n({ kind: 'ethernet' })).glyph).toBe('ethernet');
    expect(networkVisual(n({ kind: 'cellular' })).glyph).toBe('cellular');
    expect(networkVisual(n({ kind: 'other' })).glyph).toBe('other');
  });
});

describe('misc helpers', () => {
  it('counts connected Bluetooth devices', () => {
    const b: BluetoothState = { supported: true, enabled: true, devices: [{ id: '1', name: 'a', connected: true }, { id: '2', name: 'b', connected: false }, { id: '3', name: 'c', connected: true }] };
    expect(connectedBluetoothCount(b)).toBe(2);
  });
  it('visibleTrayIcons filters hidden icons and unsupported hosts', () => {
    const t: TrayState = { supported: true, icons: [{ id: '1', tooltip: '', iconUrl: '', hidden: false }, { id: '2', tooltip: '', iconUrl: '', hidden: true }] };
    expect(visibleTrayIcons(t).map((i) => i.id)).toEqual(['1']);
    expect(visibleTrayIcons({ ...t, supported: false })).toEqual([]);
    expect(visibleTrayIcons(null)).toEqual([]);
  });
  it('barSettings reads the menu bar options with safe defaults', () => {
    const s: Settings = defaultSettings();
    expect(barSettings(s)).toEqual({ translucent: true, showBatteryPercent: true, clock: { use24Hour: true, showSeconds: false, showDate: true } });
    s.menuBar = { ...s.menuBar, translucent: false, showBatteryPercent: false, use24Hour: false, showSeconds: true, showDate: false };
    expect(barSettings(s)).toEqual({ translucent: false, showBatteryPercent: false, clock: { use24Hour: false, showSeconds: true, showDate: false } });
  });
});
