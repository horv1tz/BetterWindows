/** Small pure formatters for the settings window. */
import type { AboutInfo, DiagReport, MonitorInfo } from '$lib/protocol/generated';

export interface ClockOptions { showDate: boolean; showSeconds: boolean; use24Hour: boolean }

/** The menu bar clock as it will look with the given options ("Fri Oct 23  2:05 PM"). */
export function formatMenuBarClock(o: ClockOptions, date: Date, locale: string): string {
  const time = new Intl.DateTimeFormat(locale, {
    hour: 'numeric', minute: '2-digit', ...(o.showSeconds ? { second: '2-digit' as const } : {}), hourCycle: o.use24Hour ? 'h23' : 'h12',
  }).format(date);
  if (!o.showDate) return time;
  const day = new Intl.DateTimeFormat(locale, { weekday: 'short', month: 'short', day: 'numeric' }).format(date).replace(/,/g, '');
  return `${day}  ${time}`;
}

export interface DiagSummary { total: number; failed: number; passed: number }
export function summarizeDiag(r: DiagReport): DiagSummary {
  const failed = r.checks.filter((c) => !c.ok).length;
  return { total: r.checks.length, failed, passed: r.checks.length - failed };
}

/** Plain-text report for bug reports ("Copy report"). Deliberately not localised. */
export function formatReportText(about: AboutInfo | null, report: DiagReport): string {
  const s = summarizeDiag(report);
  const lines = [
    `BetterWindows ${about?.version ?? report.version}${about?.build ? ` (build ${about.build})` : ''}`,
    `OS: ${about?.os || report.os}`,
    ...(about?.webView2 ? [`WebView2: ${about.webView2}`] : []),
    `Checks: ${s.passed}/${s.total} passed${s.failed ? `, ${s.failed} failed` : ''}`,
    '',
    ...report.checks.map((c) => `[${c.ok ? ' OK ' : 'FAIL'}] ${c.name}${c.detail ? ` - ${c.detail}` : ''}`),
  ];
  return lines.join('\n');
}

/** "Display 2 (primary) 2560x1440" style description of a monitor for pickers. */
export function monitorResolution(m: MonitorInfo): string {
  const scale = m.scale > 0 ? m.scale : 1;
  return `${Math.round(m.boundsPx.width / scale)}×${Math.round(m.boundsPx.height / scale)}`;
}

/** Aspect ratio (w/h) of a monitor for the mini illustrations, clamped to something drawable. */
export function monitorAspect(m: MonitorInfo): number {
  const r = m.boundsPx.width / Math.max(1, m.boundsPx.height);
  return Number.isFinite(r) ? Math.min(2.4, Math.max(0.6, r)) : 16 / 10;
}

/** Sort monitors left-to-right then top-to-bottom, primary first on ties (stable order for tabs). */
export function sortMonitors(list: readonly MonitorInfo[]): MonitorInfo[] {
  return [...list].sort((a, b) => a.index - b.index);
}
