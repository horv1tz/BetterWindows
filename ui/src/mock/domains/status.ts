import type {
  AudioState, BatteryState, BluetoothState, BrightnessState, MediaState, NetworkState, TrayState,
} from '../../lib/protocol/generated';
import { art } from '../fixtures';
import type { MockContext, MockHost } from '../host';

/**
 * FROZEN (scaffold): battery / network / audio / brightness / bluetooth / media / tray state + their control calls.
 * Tweak the live state from tests or the console:  window.top.__bwMockHost.status.battery = {...}; host.pushStatus()
 */
export default function register(host: MockHost) {
  const s = ((host as any).status = {
    battery: { present: true, percent: 76, charging: false, acOnline: false, minutesRemaining: 214 } as BatteryState,
    network: { kind: 'wifi', connected: true, name: 'Home-5G', signalBars: 3 } as NetworkState,
    audio: {
      volume: 0.55, muted: false, deviceId: 'spk', deviceName: 'Speakers (Realtek Audio)',
      devices: [
        { id: 'spk', name: 'Speakers (Realtek Audio)', isDefault: true },
        { id: 'hp', name: 'Headphones (WH-1000XM5)', isDefault: false },
        { id: 'hdmi', name: 'DELL U2723QE (HDMI)', isDefault: false },
      ],
    } as AudioState,
    brightness: { supported: true, level: 0.7 } as BrightnessState,
    bluetooth: {
      supported: true, enabled: true,
      devices: [
        { id: 'b1', name: 'WH-1000XM5', connected: true, batteryPercent: 82 },
        { id: 'b2', name: 'MX Master 3S', connected: true, batteryPercent: 45 },
        { id: 'b3', name: 'Keychron K2', connected: false },
      ],
    } as BluetoothState,
    media: {
      available: true, title: 'Blinding Lights', artist: 'The Weeknd', album: 'After Hours', appName: 'Spotify', playing: true,
      positionSec: 74, durationSec: 200, thumbUrl: art('after hours'), canPlay: true, canPause: true, canNext: true, canPrevious: true, canSeek: true,
    } as MediaState,
    tray: { supported: false, icons: [] } as TrayState,
  });

  const pushTo = (ctx: MockContext) => {
    host.emitTo(ctx, 'status.battery', s.battery);
    host.emitTo(ctx, 'status.network', s.network);
    host.emitTo(ctx, 'status.audio', s.audio);
    host.emitTo(ctx, 'status.brightness', s.brightness);
    host.emitTo(ctx, 'status.bluetooth', s.bluetooth);
    host.emitTo(ctx, 'media.state', s.media);
    host.emitTo(ctx, 'tray.state', s.tray);
  };
  (host as any).pushStatus = () => { for (const b of host.bridges) pushTo(b.ctx); };
  host.onSurfaceReady((ctx) => { if (ctx.surface !== 'dock' && ctx.surface !== 'settings') pushTo(ctx); });

  host.handle('audio.setVolume', (p) => { s.audio = { ...s.audio, volume: Math.min(1, Math.max(0, p.volume)), muted: false }; host.emit('status.audio', s.audio); });
  host.handle('audio.setMuted', (p) => { s.audio = { ...s.audio, muted: p.muted }; host.emit('status.audio', s.audio); });
  host.handle('audio.setDevice', (p) => {
    const d = s.audio.devices.find((x) => x.id === p.deviceId);
    if (!d) return;
    s.audio = { ...s.audio, deviceId: d.id, deviceName: d.name, devices: s.audio.devices.map((x) => ({ ...x, isDefault: x.id === d.id })) };
    host.emit('status.audio', s.audio);
  });
  host.handle('brightness.set', (p) => { s.brightness = { ...s.brightness, level: Math.min(1, Math.max(0, p.level)) }; host.emit('status.brightness', s.brightness); });
  host.handle('theme.setPreference', async (p, ctx) => { await host.dispatchCall(ctx, 'settings.patch', { patch: { appearance: { mode: p.preference } } }); });
  host.handle('media.control', (p) => {
    const m = { ...s.media };
    if (p.action === 'toggle') m.playing = !m.playing;
    else if (p.action === 'play') m.playing = true;
    else if (p.action === 'pause') m.playing = false;
    else if (p.action === 'seek' && p.positionSec !== undefined) m.positionSec = p.positionSec;
    else if (p.action === 'next' || p.action === 'previous') { m.title = p.action === 'next' ? 'Save Your Tears' : 'Starboy'; m.positionSec = 0; m.thumbUrl = art(m.title); }
    s.media = m;
    host.emit('media.state', s.media);
  });
  host.handle('tray.click', (p) => { console.info('[mock] tray.click', p.id, p.button); });
}
