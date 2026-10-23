/**
 * Menu bar clock: formatting (pure, locale aware) and a drift-free ticker.
 *
 * The text is composed from Intl parts instead of one `toLocaleString` call so the layout is the same in every language
 * ("Fri 23 Oct 15:42" / "пт 23 окт 15:42"): weekday, day, month (abbreviation dots stripped), then the time.
 */
export interface ClockOptions {
  use24Hour: boolean;
  showSeconds: boolean;
  showDate: boolean;
}

/** Intl.DateTimeFormat construction is comparatively expensive; the clock formats every second, so cache by key. */
const formatters = new Map<string, Intl.DateTimeFormat>();
function formatter(locale: string, timeZone: string | undefined, kind: string, o: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  // With no pinned zone the formatter binds to the system zone when it is created. The current UTC offset is part of the key so a
  // changed system time zone (travel, DST switch) is picked up without a restart.
  const zone = timeZone ?? `local${new Date().getTimezoneOffset()}`;
  const key = `${locale}|${zone}|${kind}`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(safeLocale(locale), { ...o, timeZone });
    formatters.set(key, f);
  }
  return f;
}

function safeLocale(locale: string): string {
  try {
    return Intl.DateTimeFormat.supportedLocalesOf([locale]).length > 0 ? locale : 'en';
  } catch {
    return 'en';
  }
}

/** ICU separates "3:42" and "PM" with U+202F (narrow no-break space); normalise to a plain space so text is stable. */
const normaliseSpaces = (s: string) => s.replace(/[  ]/g, ' ');
const stripDot = (s: string) => s.replace(/\.$/, '');

export function formatTime(date: Date, locale: string, o: Pick<ClockOptions, 'use24Hour' | 'showSeconds'>, timeZone?: string): string {
  const f = formatter(locale, timeZone, `time|${o.use24Hour}|${o.showSeconds}`, {
    hour: o.use24Hour ? '2-digit' : 'numeric',
    minute: '2-digit',
    second: o.showSeconds ? '2-digit' : undefined,
    // hourCycle (not hour12) so midnight is "00:05" and never "24:05" in any engine.
    hourCycle: o.use24Hour ? 'h23' : 'h12',
  });
  return normaliseSpaces(f.format(date));
}

/** "Fri 23 Oct" / "пт 23 окт" — weekday, day-of-month and abbreviated month in the genitive form the language needs. */
export function formatShortDate(date: Date, locale: string, timeZone?: string): string {
  const f = formatter(locale, timeZone, 'date', { weekday: 'short', day: 'numeric', month: 'short' });
  const parts = f.formatToParts(date);
  const pick = (type: string) => stripDot(parts.find((p) => p.type === type)?.value ?? '');
  return [pick('weekday'), pick('day'), pick('month')].filter(Boolean).join(' ');
}

/** The text shown in the bar. */
export function formatClock(date: Date, locale: string, o: ClockOptions, timeZone?: string): string {
  const time = formatTime(date, locale, o, timeZone);
  return o.showDate ? `${formatShortDate(date, locale, timeZone)} ${time}` : time;
}

/** Long form for the accessible name: "Friday, 23 October 2026, 15:42" (day before month in every language, like the bar). */
export function formatClockLabel(date: Date, locale: string, o: ClockOptions, timeZone?: string): string {
  const f = formatter(locale, timeZone, 'long', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' });
  const parts = f.formatToParts(date);
  const pick = (type: string) => parts.find((p) => p.type === type)?.value ?? '';
  const day = `${pick('weekday')}, ${pick('day')} ${pick('month')} ${pick('year')}`;
  return `${normaliseSpaces(day)}, ${formatTime(date, locale, o, timeZone)}`;
}

/** Slack added after the boundary so a timer that fires a hair early still lands in the new second/minute. */
export const TICK_PAD_MS = 8;

/** Milliseconds from `nowMs` to the next second (or minute) boundary, plus TICK_PAD_MS. Always in (PAD, step + PAD]. */
export function msUntilNextTick(nowMs: number, showSeconds: boolean): number {
  const step = showSeconds ? 1000 : 60_000;
  const into = ((nowMs % step) + step) % step;
  return step - into + TICK_PAD_MS;
}

export interface TickerDeps {
  now?: () => number;
  setTimer?: (fn: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

export interface ClockTicker {
  /** Pause or resume for a named reason (hidden document, hidden surface). Resuming ticks immediately. */
  setPaused(reason: string, paused: boolean): void;
  /** Re-evaluate the schedule (call after showSeconds changed) and tick now. */
  refresh(): void;
  stop(): void;
}

/**
 * Calls `onTick(Date)` once immediately and then exactly on every second/minute boundary using a chain of setTimeout
 * calls (a setInterval would drift, and would keep firing every second while only minutes are displayed).
 * While paused for any reason nothing is scheduled at all.
 */
export function createClockTicker(onTick: (now: Date) => void, getShowSeconds: () => boolean, deps: TickerDeps = {}): ClockTicker {
  const now = deps.now ?? (() => Date.now());
  const setTimer = deps.setTimer ?? ((fn, ms) => setTimeout(fn, ms));
  const clearTimer = deps.clearTimer ?? ((h) => clearTimeout(h as ReturnType<typeof setTimeout>));
  const pausedBy = new Set<string>();
  let handle: unknown = null;
  let stopped = false;

  const cancel = () => {
    if (handle !== null) clearTimer(handle);
    handle = null;
  };
  const schedule = () => {
    cancel();
    if (stopped || pausedBy.size > 0) return;
    handle = setTimer(fire, msUntilNextTick(now(), getShowSeconds()));
  };
  const fire = () => {
    handle = null;
    if (stopped || pausedBy.size > 0) return;
    onTick(new Date(now()));
    schedule();
  };
  const tickNow = () => {
    if (stopped || pausedBy.size > 0) return;
    onTick(new Date(now()));
    schedule();
  };

  tickNow();
  return {
    setPaused(reason, paused) {
      const was = pausedBy.size > 0;
      if (paused) pausedBy.add(reason);
      else pausedBy.delete(reason);
      const is = pausedBy.size > 0;
      if (is && !was) cancel();
      else if (!is && was) tickNow();
    },
    refresh: tickNow,
    stop() {
      stopped = true;
      cancel();
    },
  };
}
