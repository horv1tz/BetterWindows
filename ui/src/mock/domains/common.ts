import { PROTOCOL_VERSION, defaultFeatureFlags, type MonitorInfo, type ThemeMode } from '../../lib/protocol/generated';
import { deepMerge, type MockHost } from '../host';

/** surface.ready, settings.get/patch, theme, logging, system stubs. Owner: scaffold — extend in your own domain file instead. */
export default function register(host: MockHost) {
  const q = new URLSearchParams(location.search);
  const width = Number(q.get('w') ?? 1920);
  const height = Number(q.get('h') ?? 1080);
  const monitor: MonitorInfo = {
    id: '1', index: 0, primary: true, name: 'Mock display',
    boundsPx: { x: 0, y: 0, width, height }, workAreaPx: { x: 0, y: 0, width, height }, scale: 1,
  };

  const systemDark = () => matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  const resolveMode = (): ThemeMode => {
    const forced = q.get('theme');
    if (forced === 'dark' || forced === 'light') return forced;
    const pref = host.settings.appearance.mode;
    return pref === 'auto' ? (systemDark() ? 'dark' : 'light') : pref;
  };
  const syncTheme = () => {
    host.theme = {
      mode: resolveMode(),
      accent: host.settings.appearance.accent === 'system' ? '#0a84ff' : host.settings.appearance.accent,
      reduceMotion: host.settings.appearance.reduceMotion,
      reduceTransparency: host.settings.appearance.reduceTransparency,
    };
  };
  syncTheme();

  host.handle('surface.ready', (p, ctx) => ({
    surface: p.surface,
    monitor,
    theme: host.theme,
    settings: host.settings,
    locale: q.get('lang') ?? 'en',
    hostVersion: 'mock',
    protocolVersion: PROTOCOL_VERSION,
    features: defaultFeatureFlags(),
    platform: 'mock',
  }));

  host.handle('settings.get', () => host.settings);
  host.handle('settings.patch', (p) => {
    host.settings = deepMerge(host.settings, p.patch);
    syncTheme();
    host.emit('settings.changed', host.settings);
    host.emit('theme.changed', host.theme);
    return host.settings;
  });

  host.onUiEvent('surface.log', (p) => console[p.level === 'debug' ? 'debug' : p.level]('[ui]', p.message, p.data ?? ''));
  host.onUiEvent('surface.resize', () => {});
  host.onUiEvent('surface.shape', () => {});

  host.handle('system.about', () => ({ version: 'mock', build: 'mock', os: 'Mock OS', webView2: 'n/a' }));
  host.handle('system.open', (p) => { console.info('[mock] system.open', p.target); });
  host.handle('system.power', (p) => { console.info('[mock] system.power', p.action); });
  host.handle('system.quit', () => { console.info('[mock] system.quit'); });
  host.handle('system.showTaskbar', (p) => { console.info('[mock] system.showTaskbar', p.show); });
  host.handle('diag.report', () => ({ version: 'mock', os: 'Mock OS', checks: [{ name: 'mock', ok: true, detail: 'in-browser mock host' }] }));
}
