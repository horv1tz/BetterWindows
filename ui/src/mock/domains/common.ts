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
  let lastLang = host.settings.general.language;

  host.handle('surface.ready', (p, ctx) => ({
    surface: p.surface,
    monitor,
    theme: host.theme,
    settings: host.settings,
    locale: q.get('lang') ?? 'en',
    hostVersion: 'mock',
    protocolVersion: PROTOCOL_VERSION,
    features: defaultFeatureFlags(),
    glassOverflow: q.get('glassOverflow') !== '0',
    platform: 'mock',
  }));

  host.handle('settings.get', () => host.settings);
  host.handle('settings.patch', (p) => {
    host.settings = deepMerge(host.settings, p.patch);
    syncTheme();
    host.emit('settings.changed', host.settings);
    host.emit('theme.changed', host.theme);
    const lang = host.settings.general.language;
    if (lang !== lastLang) { lastLang = lang; host.emit('locale.changed', { locale: lang === 'auto' ? (q.get('lang') ?? 'en') : lang }); }
    return host.settings;
  });

  host.onUiEvent('surface.log', (p) => console[p.level === 'debug' ? 'debug' : p.level]('[ui]', p.message, p.data ?? ''));
  host.onUiEvent('surface.resize', () => {});
  host.onUiEvent('surface.shape', () => {});

  host.handle('settings.openWindow', (p) => { console.info('[mock] settings.openWindow', p.page ?? ''); host.emit('settings.navigate', { page: p.page }, (c) => c.surface === 'settings'); });
  host.handle('system.pickPath', (p) => {
    console.info('[mock] system.pickPath', p.kind);
    const sample = { file: 'C:\\Users\\you\\Documents\\notes.txt', folder: 'C:\\Users\\you\\Downloads', image: 'C:\\Users\\you\\Pictures\\wallpaper.jpg', app: 'C:\\Program Files\\Tool\\tool.exe' }[p.kind];
    return { paths: q.get('pick') === 'cancel' ? [] : [sample] };
  });
  host.handle('system.monitors', () => ({ monitors: [monitor, { ...monitor, id: '2', index: 1, primary: false, name: 'Mock display 2', boundsPx: { x: width, y: 0, width: 1920, height: 1080 }, workAreaPx: { x: width, y: 0, width: 1920, height: 1080 } }] }));
  host.handle('system.about', () => ({ version: 'mock', build: 'mock', os: 'Mock OS', webView2: 'n/a' }));
  host.handle('system.open', (p) => { console.info('[mock] system.open', p.target); });
  host.handle('system.power', (p) => { console.info('[mock] system.power', p.action); });
  host.handle('system.quit', () => { console.info('[mock] system.quit'); });
  host.handle('system.showTaskbar', (p) => { console.info('[mock] system.showTaskbar', p.show); });
  host.handle('diag.report', () => ({ version: 'mock', os: 'Mock OS', checks: [{ name: 'mock', ok: true, detail: 'in-browser mock host' }] }));
}
