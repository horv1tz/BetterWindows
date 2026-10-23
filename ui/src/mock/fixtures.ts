// FROZEN (scaffold). Shared fixture data for the mock host: apps, windows, icons. Use these instead of inventing your own,
// so every surface shows the same world (the dock's apps are the Launchpad's apps are Spotlight's apps).
import type { AppEntry, WindowRef } from '../lib/protocol/generated';

/** A deterministic rounded-square icon as an SVG data URL (stands in for /dyn/icon/... in the mock). */
export function icon(name: string, hue = hash(name) % 360, glyph = (name.trim()[0] ?? '?').toUpperCase()): string {
  const a = `hsl(${hue} 80% 58%)`, b = `hsl(${(hue + 40) % 360} 78% 42%)`;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="${a}"/><stop offset="1" stop-color="${b}"/></linearGradient></defs><rect width="128" height="128" rx="29" fill="url(#g)"/><text x="64" y="86" font-family="Segoe UI,Inter,sans-serif" font-size="70" font-weight="600" text-anchor="middle" fill="white" fill-opacity=".92">${glyph}</text></svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

/** A gradient "album art"/wallpaper-like image as a data URL. */
export function art(seed: string, w = 300, h = 300): string {
  const h1 = hash(seed) % 360;
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${w}" height="${h}" viewBox="0 0 ${w} ${h}"><defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="hsl(${h1} 75% 55%)"/><stop offset="1" stop-color="hsl(${(h1 + 80) % 360} 70% 35%)"/></linearGradient></defs><rect width="100%" height="100%" fill="url(#g)"/><circle cx="${w * 0.7}" cy="${h * 0.3}" r="${Math.min(w, h) * 0.22}" fill="white" fill-opacity=".18"/></svg>`;
  return `data:image/svg+xml;utf8,${encodeURIComponent(svg)}`;
}

function hash(s: string): number {
  let h = 5381;
  for (let i = 0; i < s.length; i++) h = ((h << 5) + h + s.charCodeAt(i)) | 0;
  return Math.abs(h);
}

const app = (name: string, key: string, category: string, hue?: number): AppEntry => ({
  id: `aumid:${key}`, name, aumid: key, packaged: key.includes('!'), iconUrl: icon(name, hue), category,
  path: key.includes('!') ? undefined : `C:\\Program Files\\${name}\\${name.replace(/\s/g, '')}.exe`,
});

/** ~26 installed apps, alphabetical like the real catalog. */
export const APPS: AppEntry[] = [
  app('Calculator', 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App', 'Utilities', 215),
  app('Calendar', 'microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.calendar', 'Productivity', 5),
  app('Clock', 'Microsoft.WindowsAlarms_8wekyb3d8bbwe!App', 'Utilities', 28),
  app('Discord', 'Discord', 'Social', 235),
  app('Excel', 'Microsoft.Office.EXCEL.EXE.15', 'Productivity', 145),
  app('File Explorer', 'Microsoft.Windows.Explorer', 'System', 45),
  app('Firefox', 'Mozilla.Firefox', 'Internet', 22),
  app('Mail', 'microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail', 'Productivity', 205),
  app('Microsoft Edge', 'MSEdge', 'Internet', 190),
  app('Microsoft Store', 'Microsoft.WindowsStore_8wekyb3d8bbwe!App', 'System', 210),
  app('Notepad', 'Microsoft.WindowsNotepad_8wekyb3d8bbwe!App', 'Utilities', 200),
  app('OBS Studio', 'OBS Studio', 'Media', 270),
  app('Paint', 'Microsoft.Paint_8wekyb3d8bbwe!App', 'Media', 330),
  app('Photos', 'Microsoft.Windows.Photos_8wekyb3d8bbwe!App', 'Media', 340),
  app('PowerPoint', 'Microsoft.Office.POWERPNT.EXE.15', 'Productivity', 15),
  app('Settings', 'windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel', 'System', 220),
  app('Slack', 'com.squirrel.slack.slack', 'Social', 320),
  app('Snipping Tool', 'Microsoft.ScreenSketch_8wekyb3d8bbwe!App', 'Utilities', 175),
  app('Spotify', 'SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify', 'Media', 142),
  app('Steam', 'Steam', 'Games', 205),
  app('Telegram', 'Telegram.TelegramDesktop', 'Social', 200),
  app('Terminal', 'Microsoft.WindowsTerminal_8wekyb3d8bbwe!App', 'Developer', 160),
  app('VLC media player', 'VideoLAN.VLC', 'Media', 28),
  app('Visual Studio Code', 'Microsoft.VisualStudioCode', 'Developer', 210),
  app('Weather', 'Microsoft.BingWeather_8wekyb3d8bbwe!App', 'Utilities', 195),
  app('Word', 'Microsoft.Office.WINWORD.EXE.15', 'Productivity', 222),
];

export const appById = (id: string) => APPS.find((a) => a.id === id);
export const appByName = (name: string) => APPS.find((a) => a.name === name)!;

/** Default pinned apps of the mock (settings.dock.pins is seeded from this by the dock mock). */
export const DEFAULT_PIN_NAMES = ['File Explorer', 'Microsoft Edge', 'Mail', 'Calendar', 'Photos', 'Terminal', 'Visual Studio Code', 'Spotify', 'Settings'];

let nextHandle = 1000;
export function win(appName: string, title: string, o: Partial<WindowRef> = {}): WindowRef {
  const a = appByName(appName);
  const handle = String(nextHandle++);
  return { handle, title, appId: a.id, iconUrl: a.iconUrl, minimized: false, maximized: false, active: false, elevated: false, ...o };
}
