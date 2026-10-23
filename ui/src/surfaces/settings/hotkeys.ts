/** The five global hotkeys of BetterWindows and where they live in the settings. */
import { defaultSettings, type Settings } from '$lib/protocol/generated';
import { findHotkeyConflicts } from './hotkey';
import type { SettingsPatch } from './patch';

export const HOTKEY_IDS = ['spotlight', 'launchpad', 'missionControl', 'exit', 'toggleTaskbar'] as const;
export type HotkeyId = (typeof HOTKEY_IDS)[number];

/** `exit` and `toggleTaskbar` must stay assigned (the host resets them to the defaults otherwise): they are the way out. */
export const HOTKEY_ALLOW_EMPTY: Record<HotkeyId, boolean> = { spotlight: true, launchpad: true, missionControl: true, exit: false, toggleTaskbar: false };

export function hotkeyValue(s: Settings, id: HotkeyId): string {
  return id === 'spotlight' ? s.spotlight.hotkey : s.hotkeys[id];
}
export function hotkeyDefault(id: HotkeyId): string {
  return hotkeyValue(defaultSettings(), id);
}
export function hotkeyPatch(id: HotkeyId, value: string): SettingsPatch {
  return (id === 'spotlight' ? { spotlight: { hotkey: value } } : { hotkeys: { [id]: value } }) as SettingsPatch;
}

export interface HotkeyRow {
  id: HotkeyId;
  value: string;
  defaultValue: string;
  allowEmpty: boolean;
  /** the other four, as { id, value, name } for conflict detection and messages */
  others: Array<{ id: string; value: string; name: string }>;
  /** names of the hotkeys that currently share this combination */
  conflicts: string[];
}

/** All hotkeys with their conflict state. `name` translates a string key (`hotkey.name.<id>`). */
export function hotkeyRows(s: Settings, name: (key: string) => string): HotkeyRow[] {
  const all = HOTKEY_IDS.map((id) => ({ id, value: hotkeyValue(s, id), name: name(`hotkey.name.${id}`) }));
  const clash = findHotkeyConflicts(all);
  return HOTKEY_IDS.map((id) => ({
    id,
    value: hotkeyValue(s, id),
    defaultValue: hotkeyDefault(id),
    allowEmpty: HOTKEY_ALLOW_EMPTY[id],
    others: all.filter((o) => o.id !== id),
    conflicts: (clash.get(id) ?? []).map((o) => all.find((a) => a.id === o)!.name),
  }));
}

/** Patch that puts every hotkey back to its default (minimal: only the ones that differ). */
export function resetHotkeysPatch(s: Settings): SettingsPatch {
  const out: Record<string, Record<string, string>> = {};
  for (const id of HOTKEY_IDS) {
    if (hotkeyValue(s, id) === hotkeyDefault(id)) continue;
    const key = id === 'spotlight' ? 'spotlight' : 'hotkeys';
    (out[key] ??= {})[id === 'spotlight' ? 'hotkey' : id] = hotkeyDefault(id);
  }
  return out as SettingsPatch;
}
