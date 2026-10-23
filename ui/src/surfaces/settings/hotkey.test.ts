import { describe, expect, it } from 'vitest';
import {
  canonicalHotkey, findHotkeyConflicts, formatHotkey, hotkeyParts, initialRecorder, isFunctionKey, keyFromCode, keycapGlyph, parseHotkey,
  reduceRecorder, sameHotkey, type RecorderContext, type RecorderEvent, type RecorderState,
} from './hotkey';
import { defaultSettings } from '$lib/protocol/generated';

describe('parseHotkey', () => {
  it('parses the documented examples', () => {
    for (const s of ['Ctrl+Alt+Q', 'Alt+Space', 'Win+Shift+S', 'F4', 'Ctrl+Alt+Shift+T']) expect(parseHotkey(s).ok, s).toBe(true);
  });

  it('accepts the default hotkeys of the protocol', () => {
    const d = defaultSettings();
    for (const s of [d.spotlight.hotkey, d.hotkeys.launchpad, d.hotkeys.missionControl, d.hotkeys.exit, d.hotkeys.toggleTaskbar]) {
      expect(canonicalHotkey(s), s).toBe(s);
    }
  });

  it('is case-insensitive, order-insensitive, accepts Control/Super and whitespace', () => {
    expect(canonicalHotkey('shift+CTRL+q')).toBe('Ctrl+Shift+Q');
    expect(canonicalHotkey('Control + Super + Space')).toBe('Ctrl+Win+Space');
    expect(canonicalHotkey('win+alt+ctrl+shift+pageup')).toBe('Ctrl+Alt+Shift+Win+PageUp');
    expect(canonicalHotkey('ctrl+alt+num5')).toBe('Ctrl+Alt+Num5');
    expect(canonicalHotkey('Ctrl+Ctrl+A')).toBe('Ctrl+A'); // duplicates are tolerated on input
  });

  it('knows every key of the grammar', () => {
    const keys = [
      ...'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'.split(''),
      ...Array.from({ length: 24 }, (_, i) => `F${i + 1}`),
      'Space', 'Enter', 'Tab', 'Esc', 'Backspace', 'Delete', 'Insert', 'Home', 'End', 'PageUp', 'PageDown', 'Up', 'Down', 'Left', 'Right',
      'Comma', 'Period', 'Slash', 'Backslash', 'Semicolon', 'Quote', 'Minus', 'Equals', 'Backtick', 'LeftBracket', 'RightBracket',
      ...Array.from({ length: 10 }, (_, i) => `Num${i}`),
    ];
    for (const k of keys) expect(canonicalHotkey(`Ctrl+${k}`), k).toBe(`Ctrl+${k}`);
    expect(keys).toHaveLength(36 + 24 + 15 + 11 + 10);
  });

  it('requires a modifier unless the key is F1-F24', () => {
    expect(parseHotkey('A')).toEqual({ ok: false, reason: 'noModifier' });
    expect(parseHotkey('Space')).toEqual({ ok: false, reason: 'noModifier' });
    expect(parseHotkey('F1').ok).toBe(true);
    expect(parseHotkey('F24').ok).toBe(true);
    expect(parseHotkey('F25')).toEqual({ ok: false, reason: 'unknownKey' });
    expect(parseHotkey('F0')).toEqual({ ok: false, reason: 'unknownKey' });
  });

  it('rejects garbage with a reason', () => {
    expect(parseHotkey('')).toEqual({ ok: false, reason: 'empty' });
    expect(parseHotkey('   ')).toEqual({ ok: false, reason: 'empty' });
    expect(parseHotkey('Ctrl+')).toEqual({ ok: false, reason: 'syntax' });
    expect(parseHotkey('+A')).toEqual({ ok: false, reason: 'syntax' });
    expect(parseHotkey('Ctrl++A')).toEqual({ ok: false, reason: 'syntax' });
    expect(parseHotkey('Ctrl+Alt')).toEqual({ ok: false, reason: 'modifierOnly' });
    expect(parseHotkey('Ctrl')).toEqual({ ok: false, reason: 'modifierOnly' });
    expect(parseHotkey('Meta+A')).toEqual({ ok: false, reason: 'unknownToken' });
    expect(parseHotkey('Ctrl+A+B')).toEqual({ ok: false, reason: 'unknownToken' });
    expect(parseHotkey('Ctrl+Escape')).toEqual({ ok: false, reason: 'unknownKey' });
    expect(parseHotkey('Ctrl+é')).toEqual({ ok: false, reason: 'unknownKey' });
  });

  it('canonicalHotkey: empty means unassigned, invalid is undefined', () => {
    expect(canonicalHotkey('')).toBe('');
    expect(canonicalHotkey('  ')).toBe('');
    expect(canonicalHotkey('nonsense')).toBeUndefined();
  });

  it('formatHotkey round-trips through parse', () => {
    const r = parseHotkey('alt+shift+f10');
    expect(r.ok && formatHotkey(r.value)).toBe('Alt+Shift+F10');
  });
});

describe('helpers', () => {
  it('hotkeyParts splits the canonical string', () => {
    expect(hotkeyParts('shift+ctrl+alt+t')).toEqual(['Ctrl', 'Alt', 'Shift', 'T']);
    expect(hotkeyParts('')).toEqual([]);
    expect(hotkeyParts('bogus')).toEqual([]);
  });
  it('sameHotkey ignores spelling but not emptiness', () => {
    expect(sameHotkey('alt+space', 'Alt+Space')).toBe(true);
    expect(sameHotkey('Alt+Space', 'Alt+Enter')).toBe(false);
    expect(sameHotkey('', '')).toBe(false);
    expect(sameHotkey('x', 'x')).toBe(false);
  });
  it('isFunctionKey', () => {
    expect(isFunctionKey('F1')).toBe(true);
    expect(isFunctionKey('F24')).toBe(true);
    expect(isFunctionKey('F25')).toBe(false);
    expect(isFunctionKey('Fx')).toBe(false);
  });
  it('keycapGlyph', () => {
    expect(keycapGlyph('Up')).toBe('↑');
    expect(keycapGlyph('Comma')).toBe(',');
    expect(keycapGlyph('Num5')).toBe('Num 5');
    expect(keycapGlyph('Q')).toBe('Q');
  });
  it('keyFromCode maps layout independent codes', () => {
    expect(keyFromCode('KeyQ')).toBe('Q');
    expect(keyFromCode('Digit7')).toBe('7');
    expect(keyFromCode('Numpad3')).toBe('Num3');
    expect(keyFromCode('NumpadEnter')).toBe('Enter');
    expect(keyFromCode('F12')).toBe('F12');
    expect(keyFromCode('F24')).toBe('F24');
    expect(keyFromCode('F25')).toBeUndefined();
    expect(keyFromCode('ArrowLeft')).toBe('Left');
    expect(keyFromCode('Escape')).toBe('Esc');
    expect(keyFromCode('Backquote')).toBe('Backtick');
    expect(keyFromCode('Equal')).toBe('Equals');
    expect(keyFromCode('BracketLeft')).toBe('LeftBracket');
    expect(keyFromCode('CapsLock')).toBeUndefined();
    expect(keyFromCode('MediaPlayPause')).toBeUndefined();
  });
});

describe('findHotkeyConflicts', () => {
  it('reports every id that shares a combination', () => {
    const m = findHotkeyConflicts([
      { id: 'spotlight', value: 'Alt+Space' },
      { id: 'launchpad', value: 'ctrl+alt+l' },
      { id: 'missionControl', value: 'Ctrl+Alt+L' },
      { id: 'exit', value: '' },
      { id: 'toggleTaskbar', value: '' },
    ]);
    expect(m.get('launchpad')).toEqual(['missionControl']);
    expect(m.get('missionControl')).toEqual(['launchpad']);
    expect(m.has('spotlight')).toBe(false);
    expect(m.has('exit')).toBe(false); // two unassigned hotkeys do not conflict
  });
  it('has no conflicts for the defaults', () => {
    const d = defaultSettings();
    const list = [
      { id: 'spotlight', value: d.spotlight.hotkey }, { id: 'launchpad', value: d.hotkeys.launchpad }, { id: 'missionControl', value: d.hotkeys.missionControl },
      { id: 'exit', value: d.hotkeys.exit }, { id: 'toggleTaskbar', value: d.hotkeys.toggleTaskbar },
    ];
    expect(findHotkeyConflicts(list).size).toBe(0);
  });
});

describe('recorder reducer', () => {
  const ctx: RecorderContext = { allowEmpty: true, others: [{ id: 'launchpad', value: 'Ctrl+Alt+L' }, { id: 'exit', value: 'Ctrl+Alt+Q' }] };
  const noMods = { ctrl: false, alt: false, shift: false, meta: false };
  const down = (code: string, mods: Partial<typeof noMods> = {}): RecorderEvent => ({ type: 'keydown', code, ...noMods, ...mods });
  const up = (code: string, mods: Partial<typeof noMods> = {}): RecorderEvent => ({ type: 'keyup', code, ...noMods, ...mods });
  const run = (events: RecorderEvent[], c: RecorderContext = ctx, s: RecorderState = initialRecorder()) => events.reduce((st, e) => reduceRecorder(st, e, c), s);

  it('ignores keys while idle', () => {
    expect(run([down('KeyA', { ctrl: true })]).phase).toBe('idle');
  });

  it('start -> modifiers are tracked live -> combination commits', () => {
    let s = run([{ type: 'start' }]);
    expect(s.phase).toBe('recording');
    s = run([down('ControlLeft', { ctrl: true })], ctx, s);
    expect(s.held).toEqual({ ctrl: true, alt: false, shift: false, win: false });
    expect(s.outcome).toBeUndefined();
    s = run([down('AltLeft', { ctrl: true, alt: true }), down('KeyK', { ctrl: true, alt: true })], ctx, s);
    expect(s.phase).toBe('idle');
    expect(s.outcome).toEqual({ kind: 'set', value: 'Ctrl+Alt+K' });
  });

  it('records the Windows key through metaKey and orders canonically', () => {
    const s = run([{ type: 'start' }, down('KeyS', { meta: true, shift: true })]);
    expect(s.outcome).toEqual({ kind: 'set', value: 'Shift+Win+S' });
  });

  it('keyup releases modifiers in the display', () => {
    let s = run([{ type: 'start' }, down('ShiftLeft', { shift: true })]);
    expect(s.held.shift).toBe(true);
    s = run([up('ShiftLeft')], ctx, s);
    expect(s.held.shift).toBe(false);
    expect(s.phase).toBe('recording');
  });

  it('a bare letter needs a modifier but F-keys do not', () => {
    let s = run([{ type: 'start' }, down('KeyA')]);
    expect(s).toMatchObject({ phase: 'recording', error: 'needsModifier', attempt: 'A' });
    s = run([down('F9')], ctx, s);
    expect(s.outcome).toEqual({ kind: 'set', value: 'F9' });
  });

  it('error clears when a modifier key is pressed afterwards', () => {
    let s = run([{ type: 'start' }, down('KeyA')]);
    expect(s.error).toBe('needsModifier');
    s = run([down('ControlLeft', { ctrl: true })], ctx, s);
    expect(s.error).toBeUndefined();
  });

  it('rejects conflicts with the other hotkeys and names the owner', () => {
    const s = run([{ type: 'start' }, down('KeyL', { ctrl: true, alt: true })]);
    expect(s).toMatchObject({ phase: 'recording', error: 'conflict', conflictWith: 'launchpad', attempt: 'Ctrl+Alt+L' });
    expect(s.outcome).toBeUndefined();
  });

  it('rejects reserved combinations', () => {
    expect(run([{ type: 'start' }, down('F4', { alt: true })])).toMatchObject({ error: 'reserved', attempt: 'Alt+F4' });
    expect(run([{ type: 'start' }, down('KeyL', { meta: true })])).toMatchObject({ error: 'reserved' });
    expect(run([{ type: 'start' }, down('Delete', { ctrl: true, alt: true })])).toMatchObject({ error: 'reserved', attempt: 'Ctrl+Alt+Delete' });
  });

  it('rejects keys that have no name in the grammar', () => {
    expect(run([{ type: 'start' }, down('CapsLock', { ctrl: true })])).toMatchObject({ phase: 'recording', error: 'unsupportedKey' });
    expect(run([{ type: 'start' }, down('MediaTrackNext')])).toMatchObject({ error: 'unsupportedKey' });
  });

  it('Escape cancels, Tab cancels without swallowing focus movement', () => {
    expect(run([{ type: 'start' }, down('Escape')]).outcome).toEqual({ kind: 'cancel', reason: 'escape' });
    expect(run([{ type: 'start' }, down('Tab')]).outcome).toEqual({ kind: 'cancel', reason: 'tab' });
    expect(run([{ type: 'start' }, down('Tab', { ctrl: true })]).outcome).toEqual({ kind: 'set', value: 'Ctrl+Tab' }); // modified Tab is recordable
  });

  it('Backspace/Delete clears when unassigned is allowed, otherwise explains', () => {
    expect(run([{ type: 'start' }, down('Backspace')]).outcome).toEqual({ kind: 'clear' });
    const strict: RecorderContext = { ...ctx, allowEmpty: false };
    expect(run([{ type: 'start' }, down('Delete')], strict)).toMatchObject({ phase: 'recording', error: 'emptyNotAllowed' });
    // with a modifier Backspace is just a key
    expect(run([{ type: 'start' }, down('Backspace', { ctrl: true })], strict).outcome).toEqual({ kind: 'set', value: 'Ctrl+Backspace' });
  });

  it('blur and cancel stop recording', () => {
    expect(run([{ type: 'start' }, { type: 'blur' }]).outcome).toEqual({ kind: 'cancel', reason: 'blur' });
    expect(run([{ type: 'start' }, { type: 'cancel' }]).outcome).toEqual({ kind: 'cancel', reason: 'api' });
    expect(run([{ type: 'blur' }]).phase).toBe('idle');
  });

  it('start resets previous errors and outcomes', () => {
    const s = run([{ type: 'start' }, down('KeyA'), { type: 'cancel' }, { type: 'start' }]);
    expect(s).toEqual({ phase: 'recording', held: { ctrl: false, alt: false, shift: false, win: false } });
  });
});
