import { describe, expect, it } from 'vitest';
import { defaultSettings, type Settings } from '$lib/protocol/generated';
import { HOTKEY_IDS, hotkeyRows, hotkeyValue, resetHotkeysPatch } from './hotkeys';
import { applyPatch } from './patch';

const name = (k: string) => k.replace('hotkey.name.', '');

describe('hotkey rows', () => {
  it('lists the five global hotkeys in contract order', () => {
    const rows = hotkeyRows(defaultSettings(), name);
    expect(rows.map((r) => r.id)).toEqual([...HOTKEY_IDS]);
  });

  it('marks exit and taskbar toggle as not clearable', () => {
    const rows = hotkeyRows(defaultSettings(), name);
    expect(rows.find((r) => r.id === 'exit')?.allowEmpty).toBe(false);
    expect(rows.find((r) => r.id === 'toggleTaskbar')?.allowEmpty).toBe(false);
    expect(rows.find((r) => r.id === 'spotlight')?.allowEmpty).toBe(true);
  });

  it('reports the hotkeys that share a combination by name', () => {
    const s: Settings = structuredClone(defaultSettings());
    s.hotkeys.launchpad = s.spotlight.hotkey;
    const rows = hotkeyRows(s, name);
    expect(rows.find((r) => r.id === 'launchpad')?.conflicts).toEqual(['spotlight']);
    expect(rows.find((r) => r.id === 'spotlight')?.conflicts).toEqual(['launchpad']);
    expect(rows.find((r) => r.id === 'exit')?.conflicts).toEqual([]);
  });

  it('reset patch touches only the hotkeys that differ from their defaults', () => {
    const d = defaultSettings();
    expect(resetHotkeysPatch(d)).toEqual({});
    const s: Settings = structuredClone(d);
    s.hotkeys.exit = 'Ctrl+Alt+X';
    const patch = resetHotkeysPatch(s);
    expect(patch).toEqual({ hotkeys: { exit: hotkeyValue(d, 'exit') } });
    expect(applyPatch(s, patch).hotkeys.exit).toBe(hotkeyValue(d, 'exit'));
  });

  it('spotlight lives under spotlight.hotkey, the rest under hotkeys', () => {
    const s = defaultSettings();
    expect(hotkeyValue(s, 'spotlight')).toBe(s.spotlight.hotkey);
    expect(hotkeyValue(s, 'launchpad')).toBe(s.hotkeys.launchpad);
  });
});
