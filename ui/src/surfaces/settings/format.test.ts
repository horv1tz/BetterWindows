import { describe, expect, it } from 'vitest';
import type { DiagReport, MonitorInfo } from '$lib/protocol/generated';
import { formatMenuBarClock, formatReportText, monitorAspect, monitorResolution, sortMonitors, summarizeDiag } from './format';

const date = new Date(2026, 9, 23, 14, 5, 9); // Fri Oct 23 2026 14:05:09 local

describe('formatMenuBarClock', () => {
  it('24 hour, no date, no seconds', () => {
    expect(formatMenuBarClock({ showDate: false, showSeconds: false, use24Hour: true }, date, 'en')).toBe('14:05');
  });
  it('seconds are optional', () => {
    expect(formatMenuBarClock({ showDate: false, showSeconds: true, use24Hour: true }, date, 'en')).toBe('14:05:09');
  });
  it('12 hour clock has a day period', () => {
    expect(formatMenuBarClock({ showDate: false, showSeconds: false, use24Hour: false }, date, 'en')).toMatch(/^2:05\s?PM$/i);
  });
  it('date goes first without the comma', () => {
    const s = formatMenuBarClock({ showDate: true, showSeconds: false, use24Hour: true }, date, 'en');
    expect(s).toMatch(/^Fri Oct 23 {2}14:05$/);
  });
  it('is localised', () => {
    const s = formatMenuBarClock({ showDate: true, showSeconds: false, use24Hour: true }, date, 'ru');
    expect(s).toMatch(/пт/);
    expect(s).toMatch(/14:05$/);
  });
});

describe('diagnostics helpers', () => {
  const report: DiagReport = {
    version: '1.0.0', os: 'Windows 11',
    checks: [{ name: 'WebView2', ok: true, detail: '126.0' }, { name: 'Tray', ok: false, detail: 'not supported' }, { name: 'Nothing', ok: true, detail: '' }],
  };
  it('summarizes', () => {
    expect(summarizeDiag(report)).toEqual({ total: 3, failed: 1, passed: 2 });
  });
  it('renders a copyable report', () => {
    const text = formatReportText({ version: '1.0.0', build: '42', os: 'Windows 11 24H2', webView2: '126.0' }, report);
    expect(text).toContain('BetterWindows 1.0.0 (build 42)');
    expect(text).toContain('OS: Windows 11 24H2');
    expect(text).toContain('Checks: 2/3 passed, 1 failed');
    expect(text).toContain('[ OK ] WebView2 - 126.0');
    expect(text).toContain('[FAIL] Tray - not supported');
    expect(text).toContain('[ OK ] Nothing');
    expect(text).not.toContain('Nothing -');
  });
  it('works without about info', () => {
    expect(formatReportText(null, report).split('\n')[0]).toBe('BetterWindows 1.0.0');
  });
});

describe('monitor helpers', () => {
  const mon = (id: string, index: number, w: number, h: number, scale = 1): MonitorInfo => ({
    id, index, primary: index === 0, name: id, boundsPx: { x: 0, y: 0, width: w, height: h }, workAreaPx: { x: 0, y: 0, width: w, height: h }, scale,
  });
  it('resolution is in DIPs', () => {
    expect(monitorResolution(mon('a', 0, 3840, 2160, 2))).toBe('1920×1080');
    expect(monitorResolution(mon('a', 0, 1920, 1080, 0))).toBe('1920×1080');
  });
  it('aspect is clamped and safe', () => {
    expect(monitorAspect(mon('a', 0, 1920, 1080))).toBeCloseTo(16 / 9, 5);
    expect(monitorAspect(mon('a', 0, 100, 1000))).toBe(0.6);
    expect(monitorAspect(mon('a', 0, 10000, 100))).toBe(2.4);
    expect(monitorAspect(mon('a', 0, 100, 0))).toBe(2.4);
  });
  it('sorts by index', () => {
    expect(sortMonitors([mon('b', 1, 1, 1), mon('a', 0, 1, 1)]).map((m) => m.id)).toEqual(['a', 'b']);
  });
});
