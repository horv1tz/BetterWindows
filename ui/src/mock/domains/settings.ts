// Settings window mock: diagnostics fixture, weather location search, and desktop-layer fallbacks used by the settings pane.
// Load order is alphabetical (desktop.ts before settings.ts), so the desktop/widget fallbacks below are registered only when
// the desktop domain did not register them; weather.ts loads after this file and therefore overrides the weather fallbacks.
import type { MockContext, MockHost } from '../host';
import type { DiagReport, WeatherLocation, WidgetInstance } from '../../lib/protocol/generated';

const PLACES: WeatherLocation[] = [
  { name: 'Berlin', country: 'Germany', admin: 'Berlin', latitude: 52.52, longitude: 13.41 },
  { name: 'Berlin', country: 'United States', admin: 'New Hampshire', latitude: 44.47, longitude: -71.19 },
  { name: 'Bergen', country: 'Norway', admin: 'Vestland', latitude: 60.39, longitude: 5.32 },
  { name: 'Берлин', country: 'Германия', admin: 'Берлин', latitude: 52.52, longitude: 13.41 },
];

const DIAG: DiagReport = {
  version: '0.1.0 (mock)',
  os: 'Windows 11 Pro 23H2 (mock)',
  checks: [
    { name: 'Windows version', ok: true, detail: 'Windows 11 build 22631' },
    { name: 'WebView2 runtime', ok: true, detail: '130.0.2849.80' },
    { name: 'Global hotkeys', ok: true, detail: 'Spotlight, Launchpad, Mission Control, Quit, Taskbar toggle' },
    { name: 'Escape hotkey registered', ok: true, detail: 'Ctrl+Alt+Shift+T' },
    { name: 'Shell window (Shell_TrayWnd)', ok: true, detail: 'found' },
    { name: 'Desktop layer (Progman/WorkerW)', ok: true, detail: 'attached on 2 displays' },
    { name: 'Per-monitor DPI awareness', ok: true, detail: 'Per-monitor V2' },
    { name: 'Window thumbnails (DWM)', ok: false, detail: 'DWM composition disabled by policy; Mission Control shows icons only' },
    { name: 'Autostart entry', ok: true, detail: 'HKCU\\...\\Run' },
  ],
};

export default function register(host: MockHost) {
  // A browser preview is a configured install: the wizard is shown with ?onboarding=1 (or at #/settings/onboarding).
  if (!new URLSearchParams(location.search).has('onboarding')) {
    host.settings = { ...host.settings, general: { ...host.settings.general, firstRunDone: true } };
  }
  const has = (type: string): boolean => Boolean((host as unknown as { calls?: Map<string, unknown> }).calls?.has(type));
  const changed = () => host.emit('settings.changed', host.settings);

  host.handle('diag.report', () => structuredClone(DIAG));

  // weather: always registered (weather.ts, loaded later, replaces these when present)
  host.handle('weather.search', (p) => {
    const q = p.query.trim().toLowerCase();
    return { locations: PLACES.filter((l) => l.name.toLowerCase().includes(q) || l.country?.toLowerCase().includes(q)).slice(0, 8) };
  });
  host.handle('weather.setLocation', (p, ctx: MockContext) => {
    host.settings = { ...host.settings, weather: { ...host.settings.weather, locationName: p.name, latitude: p.latitude, longitude: p.longitude } };
    changed();
    void ctx;
  });

  // desktop layer: minimal behaviour, only where the desktop domain did not register the call
  const desk = () => host.settings.desktop;
  const setDesk = (patch: Partial<typeof host.settings.desktop>) => { host.settings = { ...host.settings, desktop: { ...desk(), ...patch } }; changed(); };
  let seq = 1;
  if (!has('desktop.setWallpaper')) host.handle('desktop.setWallpaper', (p) => {
    if (p.monitorId) setDesk({ wallpapers: { ...desk().wallpapers, [p.monitorId]: p.path } });
    else setDesk({ wallpaperPath: p.path, wallpapers: {} });
  });
  if (!has('desktop.addShortcut')) host.handle('desktop.addShortcut', (p) => {
    const name = p.path.split(/[\\/]/).filter(Boolean).pop() ?? p.path;
    const sc = { id: `sc:${seq++}`, name, target: p.path, x: p.x ?? 0, y: p.y ?? 0, monitorId: p.monitorId };
    setDesk({ shortcuts: [...desk().shortcuts, sc] });
    return { id: sc.id, name, iconUrl: '', x: sc.x, y: sc.y, target: p.path, kind: 'shortcut' };
  });
  if (!has('desktop.removeShortcut')) host.handle('desktop.removeShortcut', (p) => setDesk({ shortcuts: desk().shortcuts.filter((s) => s.id !== p.id) }));
  if (!has('desktop.addWidget')) host.handle('desktop.addWidget', (p) => {
    const w: WidgetInstance = { id: `w:${seq++}`, type: p.type, size: p.size ?? 'medium', x: p.x ?? 48, y: p.y ?? 80, monitorId: p.monitorId };
    setDesk({ widgets: [...desk().widgets, w] });
    return w;
  });
  if (!has('desktop.removeWidget')) host.handle('desktop.removeWidget', (p) => setDesk({ widgets: desk().widgets.filter((w) => w.id !== p.id) }));
  if (!has('desktop.updateWidget')) host.handle('desktop.updateWidget', (p) => {
    const cur = desk().widgets.find((w) => w.id === p.id);
    if (!cur) throw new Error(`no widget ${p.id}`);
    const next = { ...cur, size: p.size ?? cur.size, config: p.config ?? cur.config };
    setDesk({ widgets: desk().widgets.map((w) => (w.id === p.id ? next : w)) });
    return next;
  });
  if (!has('desktop.moveWidget')) host.handle('desktop.moveWidget', (p) => setDesk({ widgets: desk().widgets.map((w) => (w.id === p.id ? { ...w, x: p.x, y: p.y } : w)) }));
}
