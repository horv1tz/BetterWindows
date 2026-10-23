/**
 * Hotkey strings (docs/IPC.md section 8.1) — the TypeScript twin of the C# `HotkeyParser`.
 *
 *   "Mod+Mod+Key". Modifiers: Ctrl/Control, Alt, Shift, Win/Super (case-insensitive, any order, duplicates tolerated on input);
 *   canonical output order is Ctrl+Alt+Shift+Win+Key. Keys: A-Z 0-9 F1-F24 Space Enter Tab Esc Backspace Delete Insert Home End
 *   PageUp PageDown Up Down Left Right, punctuation by name (Backtick Minus Equals LeftBracket RightBracket Backslash Semicolon
 *   Quote Comma Period Slash) and Num0-Num9. At least one modifier unless the key is F1-F24. Empty string = unassigned.
 *
 * This file also holds the press-to-record state machine used by HotkeyRecorder (a pure reducer: no DOM, fully unit-tested).
 */

export interface ParsedHotkey {
  ctrl: boolean;
  alt: boolean;
  shift: boolean;
  win: boolean;
  /** canonical key name, e.g. "Q", "F4", "PageUp", "Num5" */
  key: string;
}

export type HotkeyParseError = 'empty' | 'syntax' | 'unknownToken' | 'unknownKey' | 'modifierOnly' | 'noModifier';
export type HotkeyParseResult = { ok: true; value: ParsedHotkey } | { ok: false; reason: HotkeyParseError };

const MOD_ALIASES: Record<string, 'ctrl' | 'alt' | 'shift' | 'win'> = {
  ctrl: 'ctrl', control: 'ctrl', alt: 'alt', shift: 'shift', win: 'win', super: 'win',
};

const NAMED_KEYS = [
  'Space', 'Enter', 'Tab', 'Esc', 'Backspace', 'Delete', 'Insert', 'Home', 'End', 'PageUp', 'PageDown',
  'Up', 'Down', 'Left', 'Right',
  'Backtick', 'Minus', 'Equals', 'LeftBracket', 'RightBracket', 'Backslash', 'Semicolon', 'Quote', 'Comma', 'Period', 'Slash',
];

/** lower-case name -> canonical key name */
const KEY_CANON: ReadonlyMap<string, string> = (() => {
  const m = new Map<string, string>();
  for (let c = 65; c <= 90; c++) m.set(String.fromCharCode(c).toLowerCase(), String.fromCharCode(c));
  for (let d = 0; d <= 9; d++) { m.set(String(d), String(d)); m.set(`num${d}`, `Num${d}`); }
  for (let f = 1; f <= 24; f++) m.set(`f${f}`, `F${f}`);
  for (const k of NAMED_KEYS) m.set(k.toLowerCase(), k);
  return m;
})();

export const isFunctionKey = (key: string): boolean => /^F([1-9]|1\d|2[0-4])$/.test(key);

export function parseHotkey(input: string): HotkeyParseResult {
  const text = (input ?? '').trim();
  if (text === '') return { ok: false, reason: 'empty' };
  const tokens = text.split('+').map((s) => s.trim());
  if (tokens.some((t) => t === '')) return { ok: false, reason: 'syntax' };
  const last = tokens[tokens.length - 1].toLowerCase();
  const out: ParsedHotkey = { ctrl: false, alt: false, shift: false, win: false, key: '' };
  for (const tok of tokens.slice(0, -1)) {
    const mod = MOD_ALIASES[tok.toLowerCase()];
    if (!mod) return { ok: false, reason: 'unknownToken' };
    out[mod] = true;
  }
  if (MOD_ALIASES[last]) return { ok: false, reason: 'modifierOnly' };
  const key = KEY_CANON.get(last);
  if (!key) return { ok: false, reason: 'unknownKey' };
  out.key = key;
  if (!hasModifier(out) && !isFunctionKey(key)) return { ok: false, reason: 'noModifier' };
  return { ok: true, value: out };
}

export const hasModifier = (p: Pick<ParsedHotkey, 'ctrl' | 'alt' | 'shift' | 'win'>): boolean => p.ctrl || p.alt || p.shift || p.win;

/** Canonical string: Ctrl+Alt+Shift+Win+Key. */
export function formatHotkey(p: ParsedHotkey): string {
  const parts: string[] = [];
  if (p.ctrl) parts.push('Ctrl');
  if (p.alt) parts.push('Alt');
  if (p.shift) parts.push('Shift');
  if (p.win) parts.push('Win');
  parts.push(p.key);
  return parts.join('+');
}

/** Canonical form of a valid hotkey, '' for unassigned, undefined when the string is not a valid hotkey. */
export function canonicalHotkey(input: string): string | undefined {
  if ((input ?? '').trim() === '') return '';
  const r = parseHotkey(input);
  return r.ok ? formatHotkey(r.value) : undefined;
}

/** Tokens for keycap rendering: ['Ctrl','Alt','Shift','T']. Empty for unassigned or invalid input. */
export function hotkeyParts(input: string): string[] {
  const r = parseHotkey(input);
  return r.ok ? formatHotkey(r.value).split('+') : [];
}

export function sameHotkey(a: string, b: string): boolean {
  const ca = canonicalHotkey(a), cb = canonicalHotkey(b);
  return ca !== undefined && cb !== undefined && ca !== '' && ca === cb;
}

/** Glyph for a keycap (arrows and punctuation read better as symbols); everything else is returned unchanged. */
export function keycapGlyph(token: string): string {
  const map: Record<string, string> = {
    Up: '↑', Down: '↓', Left: '←', Right: '→',
    Backtick: '`', Minus: '-', Equals: '=', LeftBracket: '[', RightBracket: ']', Backslash: '\\', Semicolon: ';', Quote: "'",
    Comma: ',', Period: '.', Slash: '/',
  };
  return map[token] ?? token.replace(/^Num(\d)$/, 'Num $1');
}

// ---------------------------------------------------------------------------------------------------------------------
// Keyboard event -> key name

const MODIFIER_CODES = new Set(['ControlLeft', 'ControlRight', 'ShiftLeft', 'ShiftRight', 'AltLeft', 'AltRight', 'MetaLeft', 'MetaRight', 'OSLeft', 'OSRight', 'AltGraph']);
export const isModifierCode = (code: string): boolean => MODIFIER_CODES.has(code);

const CODE_NAMES: Record<string, string> = {
  Space: 'Space', Enter: 'Enter', NumpadEnter: 'Enter', Tab: 'Tab', Escape: 'Esc', Backspace: 'Backspace', Delete: 'Delete', Insert: 'Insert',
  Home: 'Home', End: 'End', PageUp: 'PageUp', PageDown: 'PageDown',
  ArrowUp: 'Up', ArrowDown: 'Down', ArrowLeft: 'Left', ArrowRight: 'Right',
  Backquote: 'Backtick', Minus: 'Minus', Equal: 'Equals', BracketLeft: 'LeftBracket', BracketRight: 'RightBracket', Backslash: 'Backslash',
  Semicolon: 'Semicolon', Quote: 'Quote', Comma: 'Comma', Period: 'Period', Slash: 'Slash',
};

/** `KeyboardEvent.code` (layout independent, like RegisterHotKey virtual keys) -> hotkey key name, undefined if not representable. */
export function keyFromCode(code: string): string | undefined {
  if (CODE_NAMES[code]) return CODE_NAMES[code];
  let m = /^Key([A-Z])$/.exec(code);
  if (m) return m[1];
  m = /^Digit(\d)$/.exec(code);
  if (m) return m[1];
  m = /^Numpad(\d)$/.exec(code);
  if (m) return `Num${m[1]}`;
  m = /^F(\d{1,2})$/.exec(code);
  if (m && Number(m[1]) >= 1 && Number(m[1]) <= 24) return `F${m[1]}`;
  return undefined;
}

// ---------------------------------------------------------------------------------------------------------------------
// Reserved combinations: Windows never delivers them to RegisterHotKey (or they would lock the user out).

export const RESERVED_HOTKEYS: readonly string[] = ['Ctrl+Alt+Delete', 'Win+L', 'Alt+F4', 'Alt+Tab', 'Ctrl+Shift+Esc', 'Ctrl+Esc', 'Alt+Esc', 'Win+Tab'];
export const isReservedHotkey = (canonical: string): boolean => RESERVED_HOTKEYS.includes(canonical);

// ---------------------------------------------------------------------------------------------------------------------
// Recorder state machine

export type RecorderError = 'needsModifier' | 'unsupportedKey' | 'conflict' | 'reserved' | 'emptyNotAllowed';
export interface HeldMods { ctrl: boolean; alt: boolean; shift: boolean; win: boolean }

export type RecorderOutcome =
  | { kind: 'set'; value: string }
  | { kind: 'clear' }
  | { kind: 'cancel'; reason: 'escape' | 'blur' | 'tab' | 'api' };

export interface RecorderState {
  phase: 'idle' | 'recording';
  held: HeldMods;
  /** last rejected / not yet accepted combination (canonical) to show next to the error */
  attempt?: string;
  error?: RecorderError;
  /** id of the other hotkey that already uses `attempt` */
  conflictWith?: string;
  /** set exactly when a recording finished (phase is 'idle' again) */
  outcome?: RecorderOutcome;
}

export interface RecorderKeyEvent {
  code: string;
  ctrl: boolean;
  alt: boolean;
  shift: boolean;
  /** KeyboardEvent.metaKey = the Windows key */
  meta: boolean;
}
export type RecorderEvent =
  | { type: 'start' }
  | { type: 'cancel' }
  | { type: 'blur' }
  | ({ type: 'keydown' } & RecorderKeyEvent)
  | ({ type: 'keyup' } & RecorderKeyEvent);

export interface RecorderContext {
  /** Unassigned (empty) is a legal value for this hotkey (Backspace/Delete clears it). */
  allowEmpty: boolean;
  /** The OTHER hotkeys of the app (id + current value); a recorded combination equal to one of them is rejected. */
  others: ReadonlyArray<{ id: string; value: string }>;
}

const NO_MODS: HeldMods = { ctrl: false, alt: false, shift: false, win: false };
export const initialRecorder = (): RecorderState => ({ phase: 'idle', held: { ...NO_MODS } });

const modsOf = (e: RecorderKeyEvent): HeldMods => ({ ctrl: e.ctrl, alt: e.alt, shift: e.shift, win: e.meta });

export function reduceRecorder(state: RecorderState, ev: RecorderEvent, ctx: RecorderContext): RecorderState {
  switch (ev.type) {
    case 'start':
      return { phase: 'recording', held: { ...NO_MODS } };
    case 'cancel':
      return state.phase === 'recording' ? { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'cancel', reason: 'api' } } : state;
    case 'blur':
      return state.phase === 'recording' ? { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'cancel', reason: 'blur' } } : state;
    case 'keyup':
      return state.phase === 'recording' ? { ...state, held: modsOf(ev), outcome: undefined } : state;
    case 'keydown': {
      if (state.phase !== 'recording') return state;
      const held = modsOf(ev);
      if (isModifierCode(ev.code)) return { phase: 'recording', held };
      const anyMod = hasModifier(held);
      if (!anyMod) {
        if (ev.code === 'Escape') return { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'cancel', reason: 'escape' } };
        if (ev.code === 'Tab') return { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'cancel', reason: 'tab' } };
        if (ev.code === 'Backspace' || ev.code === 'Delete') {
          return ctx.allowEmpty
            ? { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'clear' } }
            : { phase: 'recording', held, error: 'emptyNotAllowed' };
        }
      }
      const key = keyFromCode(ev.code);
      if (!key) return { phase: 'recording', held, error: 'unsupportedKey' };
      const parsed: ParsedHotkey = { ...held, key };
      const attempt = formatHotkey(parsed);
      if (!anyMod && !isFunctionKey(key)) return { phase: 'recording', held, attempt, error: 'needsModifier' };
      if (isReservedHotkey(attempt)) return { phase: 'recording', held, attempt, error: 'reserved' };
      const clash = ctx.others.find((o) => sameHotkey(o.value, attempt));
      if (clash) return { phase: 'recording', held, attempt, error: 'conflict', conflictWith: clash.id };
      return { phase: 'idle', held: { ...NO_MODS }, outcome: { kind: 'set', value: attempt } };
    }
  }
}

/** Ids of hotkeys that share the same (non-empty, valid) combination with another hotkey. */
export function findHotkeyConflicts(hotkeys: ReadonlyArray<{ id: string; value: string }>): Map<string, string[]> {
  const byCombo = new Map<string, string[]>();
  for (const h of hotkeys) {
    const c = canonicalHotkey(h.value);
    if (!c) continue;
    byCombo.set(c, [...(byCombo.get(c) ?? []), h.id]);
  }
  const out = new Map<string, string[]>();
  for (const ids of byCombo.values()) {
    if (ids.length < 2) continue;
    for (const id of ids) out.set(id, ids.filter((o) => o !== id));
  }
  return out;
}
