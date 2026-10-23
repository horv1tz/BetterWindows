import { BridgeError } from '../../lib/ipc/bridge';
import { PROTOCOL_VERSION, SETTINGS_LIMITS, defaultFeatureFlags, defaultSettings, type MonitorInfo, type Settings, type ThemeMode } from '../../lib/protocol/generated';
import { deepMerge, unknownKeys, type MockHost } from '../host';

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

  host.handle('surface.ready', (p, ctx) => {
    if (p.surface !== ctx.surface) throw new BridgeError('badRequest', `surface.ready identity mismatch: page claims '${p.surface}', host attached '${ctx.surface}'`);
    return {
    surface: ctx.surface,
    monitor,
    theme: host.theme,
    settings: host.settings,
    locale: q.get('lang') ?? 'en',
    hostVersion: 'mock',
    protocolVersion: PROTOCOL_VERSION,
    features: defaultFeatureFlags(),
    glassOverflow: q.get('glassOverflow') !== '0',
    platform: 'mock',
    };
  });

  // state producers (pushed after surface.ready exactly like IStatePublisher does)
  host.provideState('settings.changed', () => host.settings);
  host.provideState('theme.changed', () => host.theme);
  host.provideState('locale.changed', () => ({ locale: effectiveLocale() }));

  const effectiveLocale = () => (host.settings.general.language === 'auto' ? (q.get('lang') ?? 'en') : host.settings.general.language);
  host.handle('settings.get', () => host.settings);
  host.handle('settings.patch', (p) => {
    const bad = unknownKeys(defaultSettings(), p.patch);
    if (bad.length) throw new BridgeError('badRequest', `unknown settings key(s): ${bad.join(', ')}`);
    if (p.patch && typeof p.patch === 'object' && 'version' in (p.patch as object)) throw new BridgeError('badRequest', 'version is not patchable');
    host.settings = sanitize(deepMergeWithDefaults(host.settings, p.patch));
    syncTheme();
    host.emit('settings.changed', host.settings);
    host.emit('theme.changed', host.theme);
    const lang = host.settings.general.language;
    if (lang !== lastLang) { lastLang = lang; host.emit('locale.changed', { locale: effectiveLocale() }); }
    return host.settings;
  });

  host.onUiEvent('surface.log', (p) => console[p.level === 'debug' ? 'debug' : p.level]('[ui]', p.message, p.data ?? ''));
  host.onUiEvent('surface.resize', () => {});
  host.onUiEvent('surface.shape', () => {});

  /** RFC 7386 + 'null resets to the default' (what SettingsValidator does on the real host). */
  const deepMergeWithDefaults = (cur: Settings, patch: unknown): Settings => {
    const merged: any = deepMerge(cur, patch);
    const defaults: any = defaultSettings();
    const restore = (m: any, d: any, pt: any) => {
      if (pt === null || typeof pt !== 'object' || Array.isArray(pt)) return;
      for (const k of Object.keys(pt)) {
        if (pt[k] === null && d && k in d && Object.keys(d).length > 0) m[k] = d[k];
        else if (typeof pt[k] === 'object' && pt[k] !== null && !Array.isArray(pt[k]) && m[k] && d) restore(m[k], d[k], pt[k]);
      }
    };
    restore(merged, defaults, patch);
    return merged;
  };
  /** Clamp numeric limits declared in protocol/settings.json (SETTINGS_LIMITS): the same table the C# SettingsValidator uses. */
  const sanitize = (s: Settings): Settings => {
    const clamp = (v: number, key: string, fb: number) => { const l = SETTINGS_LIMITS[key]; if (!Number.isFinite(v)) return fb; return Math.min(l?.max ?? v, Math.max(l?.min ?? v, v)); };
    s.dock.iconSize = clamp(s.dock.iconSize, 'DockSettings.iconSize', 56);
    s.dock.magnifiedSize = Math.max(s.dock.iconSize, clamp(s.dock.magnifiedSize, 'DockSettings.magnifiedSize', 88));
    s.spotlight.maxResults = clamp(s.spotlight.maxResults, 'SpotlightSettings.maxResults', 9);
    s.hotCorners.dwellMs = clamp(s.hotCorners.dwellMs, 'HotCornerSettings.dwellMs', 150);
    s.desktop.iconSize = clamp(s.desktop.iconSize, 'DesktopSettings.iconSize', 64);
    if (!new RegExp(SETTINGS_LIMITS['AppearanceSettings.accent'].pattern!).test(s.appearance.accent)) s.appearance.accent = 'system';
    if (!new RegExp(SETTINGS_LIMITS['GeneralSettings.language'].pattern!).test(s.general.language)) s.general.language = 'auto';
    return s;
  };

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
