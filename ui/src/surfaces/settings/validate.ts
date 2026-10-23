/** Input validation / clamping helpers for the settings controls. Pure; mirrors the rules of docs/IPC.md sections 12 and 14 so the UI rejects early. */
import { SETTINGS_LIMITS } from '$lib/protocol/generated';

/** Numeric limits from the generated table (the same source as the C# validator). */
export function rangeOf(key: string, fallback: { min: number; max: number }): { min: number; max: number } {
  const l = SETTINGS_LIMITS[key];
  return { min: l?.min ?? fallback.min, max: l?.max ?? fallback.max };
}
export const maxLengthOf = (key: string, fallback: number): number => SETTINGS_LIMITS[key]?.maxLength ?? fallback;

export function clamp(v: number, min: number, max: number): number {
  if (!Number.isFinite(v)) return min;
  return Math.min(max, Math.max(min, v));
}

/** Clamp and snap to `step` counted from `min` (so 32..128 step 4 gives 32, 36, ...). */
export function clampStep(v: number, min: number, max: number, step = 1): number {
  const c = clamp(v, min, max);
  const snapped = min + Math.round((c - min) / step) * step;
  return clamp(Math.round(snapped * 1e6) / 1e6, min, max);
}

/** Parse the text of a number field; undefined when it is not a finite number. */
export function parseNumber(text: string): number | undefined {
  const t = text.trim().replace(',', '.');
  if (t === '') return undefined;
  const n = Number(t);
  return Number.isFinite(n) ? n : undefined;
}

// ---------------------------------------------------------------------------------------------------------------------
// Web search URL template

export type SearchUrlError = 'empty' | 'tooLong' | 'scheme' | 'noPlaceholder' | 'multiPlaceholder' | 'invalidUrl';
export type SearchUrlResult = { ok: true; value: string } | { ok: false; reason: SearchUrlError };

export const QUERY_PLACEHOLDER = '{query}';

/** http(s) URL containing EXACTLY ONE `{query}` (substituted by the host with encodeURIComponent). */
export function validateSearchUrl(input: string, maxLength = maxLengthOf('SpotlightSettings.webSearchUrl', 512)): SearchUrlResult {
  const value = (input ?? '').trim();
  if (value === '') return { ok: false, reason: 'empty' };
  if (value.length > maxLength) return { ok: false, reason: 'tooLong' };
  if (!/^https?:\/\//i.test(value)) return { ok: false, reason: 'scheme' };
  const count = value.split(QUERY_PLACEHOLDER).length - 1;
  if (count === 0) return { ok: false, reason: 'noPlaceholder' };
  if (count > 1) return { ok: false, reason: 'multiPlaceholder' };
  // the WHATWG parser is lenient ("https:///x" has host "x"): require an authority and no whitespace/control characters ourselves
  if (!/^https?:\/\/[^\/?#\s]+/i.test(value) || /[\s\u0000-\u001f]/.test(value)) return { ok: false, reason: 'invalidUrl' };
  try {
    const u = new URL(value.replace(QUERY_PLACEHOLDER, 'x'));
    if (!u.hostname) return { ok: false, reason: 'invalidUrl' };
  } catch {
    return { ok: false, reason: 'invalidUrl' };
  }
  return { ok: true, value };
}

/** What a search for `query` opens. */
export const previewSearchUrl = (template: string, query: string): string => template.replace(QUERY_PLACEHOLDER, encodeURIComponent(query));

// ---------------------------------------------------------------------------------------------------------------------
// Local paths typed by the user (the host re-validates with InputPolicy; this only avoids pointless round trips)

export type PathError = 'empty' | 'tooLong' | 'notAbsolute' | 'unc' | 'invalidChars' | 'badExtension';
export type PathResult = { ok: true; value: string } | { ok: false; reason: PathError };

export const IMAGE_EXTENSIONS = ['png', 'jpg', 'jpeg', 'bmp', 'webp', 'gif'] as const;

export function validateLocalPath(input: string, opts: { extensions?: readonly string[]; maxLength?: number } = {}): PathResult {
  let value = (input ?? '').trim();
  // users paste paths copied with "Copy as path" (quoted)
  if (value.length >= 2 && value.startsWith('"') && value.endsWith('"')) value = value.slice(1, -1).trim();
  if (value === '') return { ok: false, reason: 'empty' };
  if (value.length > (opts.maxLength ?? 4096)) return { ok: false, reason: 'tooLong' };
  if (value.startsWith('\\\\') || value.startsWith('//')) return { ok: false, reason: 'unc' };
  if (!/^[A-Za-z]:[\\/]/.test(value)) return { ok: false, reason: 'notAbsolute' };
  // eslint-disable-next-line no-control-regex
  if (/[\u0000-\u001f<>|"*?]/.test(value.slice(2))) return { ok: false, reason: 'invalidChars' };
  if (opts.extensions) {
    const ext = /\.([A-Za-z0-9]+)$/.exec(value)?.[1]?.toLowerCase();
    if (!ext || !opts.extensions.includes(ext)) return { ok: false, reason: 'badExtension' };
  }
  return { ok: true, value };
}

/** File or folder name of a Windows path ("C:\\a\\b.txt" -> "b.txt", "C:\\a\\" -> "a"). */
export function baseName(path: string): string {
  const parts = path.split(/[\\/]+/).filter(Boolean);
  return parts[parts.length - 1] ?? path;
}

// ---------------------------------------------------------------------------------------------------------------------
// Accent colours (pattern from SETTINGS_LIMITS: ^(system|#rrggbb[aa])$)

export const ACCENT_PATTERN = new RegExp(SETTINGS_LIMITS['AppearanceSettings.accent']?.pattern ?? '^(system|#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?)$');
export const isValidAccent = (v: string): boolean => ACCENT_PATTERN.test(v);

/** "#ABC" / "abcdef" / "#AABBCCDD" -> "#aabbcc" (alpha dropped), null when not a colour. */
export function normalizeHex(input: string): string | null {
  const t = input.trim().replace(/^#/, '').toLowerCase();
  if (/^[0-9a-f]{3}$/.test(t)) return '#' + t.split('').map((c) => c + c).join('');
  if (/^[0-9a-f]{6}([0-9a-f]{2})?$/.test(t)) return '#' + t.slice(0, 6);
  return null;
}
