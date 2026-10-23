// Pure text helpers shared by Spotlight (match emphasis) and Launchpad (live filter).
// Folding is case- and diacritics-insensitive and works for Cyrillic ("ё" matches "е", "й" matches "и").

/** A string folded for matching, with a map from folded UTF-16 index back to the original string. */
export interface Folded {
  text: string;
  /** folded index -> start index of the originating character in the original string. */
  starts: number[];
  /** folded index -> end index (exclusive) of the originating character in the original string. */
  ends: number[];
}

const MARKS = /\p{M}+/gu;

function foldChar(ch: string): string {
  return ch.normalize('NFD').replace(MARKS, '').toLowerCase();
}

/** Fold a string and remember where each folded character came from. */
export function foldWithMap(s: string): Folded {
  let text = '';
  const starts: number[] = [];
  const ends: number[] = [];
  let i = 0;
  for (const ch of s) {
    const f = foldChar(ch);
    for (let k = 0; k < f.length; k++) {
      starts.push(i);
      ends.push(i + ch.length);
    }
    text += f;
    i += ch.length;
  }
  return { text, starts, ends };
}

/** Fold only (no index map). */
export function fold(s: string): string {
  return foldWithMap(s).text;
}

/** Folded, de-duplicated, non-empty query tokens, longest first. */
export function queryTokens(query: string): string[] {
  const seen = new Set<string>();
  for (const part of fold(query).split(/\s+/)) if (part) seen.add(part);
  return [...seen].sort((a, b) => b.length - a.length);
}

export interface Range { start: number; end: number }

const isWordChar = (c: string | undefined) => !!c && /[\p{L}\p{N}]/u.test(c);

function findToken(hay: string, token: string): Range | null {
  // Prefer an occurrence at a word start ("code" in "Visual Studio Code" rather than inside "decode").
  let first = -1;
  let from = 0;
  for (;;) {
    const at = hay.indexOf(token, from);
    if (at < 0) break;
    if (first < 0) first = at;
    if (at === 0 || !isWordChar(hay[at - 1])) return { start: at, end: at + token.length };
    from = at + 1;
  }
  if (first >= 0) return { start: first, end: first + token.length };
  return null;
}

/** Characters of `token` found in order inside `hay` (initials first: "vsc" -> Visual Studio Code). Null if not a subsequence. */
function findSubsequence(hay: string, token: string): Range[] | null {
  if (token.length < 2) return null;
  const out: number[] = [];
  let pos = 0;
  for (const ch of token) {
    // prefer a word start, else the next occurrence
    let at = -1;
    for (let j = hay.indexOf(ch, pos); j >= 0; j = hay.indexOf(ch, j + 1)) {
      if (j === 0 || !isWordChar(hay[j - 1])) { at = j; break; }
      if (at < 0) at = j;
    }
    if (at < 0) return null;
    out.push(at);
    pos = at + 1;
  }
  return out.map((i) => ({ start: i, end: i + 1 }));
}

function mergeRanges(ranges: Range[]): Range[] {
  const sorted = ranges.filter((r) => r.end > r.start).sort((a, b) => a.start - b.start || a.end - b.end);
  const out: Range[] = [];
  for (const r of sorted) {
    const last = out[out.length - 1];
    if (last && r.start <= last.end) last.end = Math.max(last.end, r.end);
    else out.push({ ...r });
  }
  return out;
}

/** Ranges (UTF-16 offsets into `text`) that match the query tokens. */
export function matchRanges(text: string, query: string): Range[] {
  const tokens = queryTokens(query);
  if (!tokens.length || !text) return [];
  const f = foldWithMap(text);
  const folded: Range[] = [];
  for (const token of tokens) {
    const hit = findToken(f.text, token);
    if (hit) folded.push(hit);
    else {
      const sub = findSubsequence(f.text, token);
      if (sub) folded.push(...sub);
    }
  }
  const mapped: Range[] = folded.map((r) => ({ start: f.starts[r.start], end: f.ends[r.end - 1] }));
  return mergeRanges(mapped);
}

export interface Segment { text: string; match: boolean }

/** Split `text` into alternating plain/matching segments for rendering with emphasis. */
export function highlightSegments(text: string, query: string): Segment[] {
  const ranges = matchRanges(text, query);
  if (!ranges.length) return text ? [{ text, match: false }] : [];
  const out: Segment[] = [];
  let at = 0;
  for (const r of ranges) {
    if (r.start > at) out.push({ text: text.slice(at, r.start), match: false });
    out.push({ text: text.slice(r.start, r.end), match: true });
    at = r.end;
  }
  if (at < text.length) out.push({ text: text.slice(at), match: false });
  return out;
}

/** True when every query token is contained (folded) in `text`. Empty query matches everything. */
export function containsAllTokens(text: string, query: string): boolean {
  const tokens = queryTokens(query);
  if (!tokens.length) return true;
  const f = fold(text);
  return tokens.every((t) => f.includes(t));
}

/**
 * "Fuzzy-ish contains" used by the Launchpad filter: every token is a substring, or (for single tokens of 2+ chars)
 * a subsequence of the word initials ("vsc" -> Visual Studio Code). Returns a score (higher = better, 0 = no match).
 */
export function fuzzyScore(text: string, query: string): number {
  const tokens = queryTokens(query);
  if (!tokens.length) return 1;
  const f = fold(text);
  let total = 0;
  for (const token of tokens) {
    const at = f.indexOf(token);
    if (at >= 0) {
      const wordStart = at === 0 || !isWordChar(f[at - 1]);
      total += (at === 0 ? 100 : wordStart ? 80 : 50) + Math.min(20, token.length);
      continue;
    }
    const words = f.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
    const initials = words.map((w) => w[0]).join('');
    if (token.length >= 2 && initials.includes(token)) { total += 30; continue; }
    return 0;
  }
  return total;
}
