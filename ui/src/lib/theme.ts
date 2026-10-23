import type { ThemeState } from './protocol/generated';

/** Applies theme tokens to <html>. Called at bootstrap and on every theme.changed. */
export function applyTheme(t: ThemeState) {
  const root = document.documentElement;
  root.dataset.theme = t.mode;
  root.style.setProperty('--accent', t.accent);
  root.dataset.reduceMotion = String(t.reduceMotion);
  root.dataset.reduceTransparency = String(t.reduceTransparency);
}
