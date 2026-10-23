/**
 * settings.patch helpers (RFC 7386 JSON Merge Patch, docs/IPC.md section 12), pure and unit-tested.
 *  - `applyPatch`: what the host will do with a patch (used for the optimistic UI; `null` restores the default / removes a map entry)
 *  - `diffPatch`: the MINIMAL patch that turns one value into another (objects recurse, arrays/primitives replace, removed map keys -> null)
 *  - `resetAllPatch`: the patch for "Reset all settings" (minimal diff to `defaultSettings()`)
 */
import { defaultSettings, type Settings } from '$lib/protocol/generated';

export type DeepPartial<T> = T extends readonly unknown[] ? T : T extends object ? { [K in keyof T]?: DeepPartial<T[K]> } : T;
/** A merge patch against Settings. Map entries may be removed with `null` through the `mapEntry` helpers. */
export type SettingsPatch = DeepPartial<Settings>;

export const isPlainObject = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v);

export function deepEqual(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((x, i) => deepEqual(x, b[i]));
  if (isPlainObject(a) && isPlainObject(b)) {
    const ka = Object.keys(a), kb = Object.keys(b);
    return ka.length === kb.length && ka.every((k) => k in b && deepEqual(a[k], b[k]));
  }
  return false;
}

export const clone = <T>(v: T): T => (v === undefined ? v : (JSON.parse(JSON.stringify(v)) as T));

/**
 * Apply a merge patch the way the host does: objects merge recursively, arrays and primitives replace, `null` deletes the key;
 * a deleted key that has a default (non-map field) takes its default, a deleted map entry is removed.
 */
export function applyPatch<T>(current: T, patch: unknown, defaults: unknown = defaultSettings()): T {
  if (!isPlainObject(patch)) return clone(patch) as T;
  const base: Record<string, unknown> = isPlainObject(current) ? { ...current } : {};
  const defs = isPlainObject(defaults) ? defaults : {};
  for (const [k, v] of Object.entries(patch)) {
    if (v === null) {
      if (k in defs && defs[k] !== undefined && defs[k] !== null) base[k] = clone(defs[k]);
      else delete base[k];
    } else if (isPlainObject(v)) {
      base[k] = applyPatch(base[k], v, defs[k]);
    } else {
      base[k] = clone(v);
    }
  }
  return base as T;
}

/** Minimal merge patch that turns `from` into `to`; undefined when they are equal. */
export function diffPatch(from: unknown, to: unknown): Record<string, unknown> | undefined {
  if (!isPlainObject(from) || !isPlainObject(to)) return undefined;
  const out: Record<string, unknown> = {};
  for (const k of Object.keys(to)) {
    const a = from[k], b = to[k];
    if (isPlainObject(a) && isPlainObject(b)) {
      const sub = diffPatch(a, b);
      if (sub) out[k] = sub;
    } else if (!deepEqual(a, b)) {
      out[k] = clone(b);
    }
  }
  for (const k of Object.keys(from)) if (!(k in to)) out[k] = null; // removed map entry
  return Object.keys(out).length ? out : undefined;
}

/** `patchAt('dock.iconSize', 72)` -> `{ dock: { iconSize: 72 } }` */
export function patchAt(path: string, value: unknown): Record<string, unknown> {
  const keys = path.split('.');
  let out: unknown = value;
  for (let i = keys.length - 1; i >= 0; i--) out = { [keys[i]]: out };
  return out as Record<string, unknown>;
}

/**
 * "Reset all settings": the minimal patch from `current` to the defaults. `version` is never patchable and `general.firstRunDone`
 * is kept (a reset must not bring the first-run wizard back by itself).
 */
export function resetAllPatch(current: Settings, defaults: Settings = defaultSettings()): SettingsPatch {
  const target = clone(defaults);
  target.version = current.version;
  target.general.firstRunDone = current.general.firstRunDone;
  return (diffPatch(current, target) ?? {}) as SettingsPatch;
}

// ---- builders for the non-trivial fields (maps, arrays) ---------------------------------------------------------------

/** Set (path string) or remove (null) one entry of `desktop.wallpapers`. */
export const wallpaperEntryPatch = (monitorId: string, path: string | null): SettingsPatch =>
  ({ desktop: { wallpapers: { [monitorId]: path } } }) as unknown as SettingsPatch;

/** Remove all desktop-folder placements (icons return to their automatic positions). */
export const clearPlacementsPatch = (): SettingsPatch => ({ desktop: { placements: null } }) as unknown as SettingsPatch;

/** Move a provider within the ordered list of enabled providers (returns the same array when nothing changes). */
export function moveItem<T>(list: readonly T[], from: number, to: number): T[] {
  if (from < 0 || from >= list.length) return [...list];
  const target = Math.max(0, Math.min(list.length - 1, to));
  if (from === target) return [...list];
  const copy = [...list];
  const [item] = copy.splice(from, 1);
  copy.splice(target, 0, item);
  return copy;
}

/** Enable/disable a provider keeping the user's order: enabled ones first in their order; a newly enabled provider is appended. */
export function toggleProvider(enabled: readonly string[], id: string, on: boolean): string[] {
  const has = enabled.includes(id);
  if (on) return has ? [...enabled] : [...enabled, id];
  return enabled.filter((p) => p !== id);
}
