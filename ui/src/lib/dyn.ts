// Mirror of BetterWindows.Core.Protocol.DynUrls (C#). Keep identical; see docs/IPC.md section 8.
// The generated protocol already hands the UI ready-made URLs (iconUrl, thumbUrl, ...) — use these only when you
// must build one yourself (e.g. a window icon for a window handle).
const enc = encodeURIComponent;
export const dyn = {
  appIcon: (appId: string, size = 128) => `/dyn/icon/app/${enc(appId)}?s=${size}`,
  windowIcon: (handle: string, size = 128) => `/dyn/icon/window/${handle}?s=${size}`,
  pathIcon: (path: string, size = 128) => `/dyn/icon/path/${enc(path)}?s=${size}`,
  windowThumb: (handle: string, width = 480, rev = 0) => `/dyn/thumb/${handle}?w=${width}&rev=${rev}`,
  mediaArt: (rev: number) => `/dyn/art/${rev}`,
  wallpaper: (monitorId: string, rev = 0) => `/dyn/wallpaper/${enc(monitorId)}?rev=${rev}`,
  trayIcon: (id: string, rev = 0) => `/dyn/tray/${enc(id)}?rev=${rev}`,
};
