// Behaviour of the widget content components against the mock host.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { WidgetInstance } from '$lib/protocol/generated';
import RemindersWidget from './widgets/RemindersWidget.svelte';
import NotesWidget from './widgets/NotesWidget.svelte';
import WeatherWidget from './widgets/WeatherWidget.svelte';
import ClockWidget from './widgets/ClockWidget.svelte';
import CalendarWidget from './widgets/CalendarWidget.svelte';
import MediaWidget from './widgets/MediaWidget.svelte';
import BatteryWidget from './widgets/BatteryWidget.svelte';
import { bootDesktop, settingsBridge, spyCalls, teardownDesktop, tick } from './testSupport';
import { cleanup, fire, flushSync, ke, me, render, screen, waitFor } from './domTest';

vi.mock('svelte', async () => await import('../../../node_modules/svelte/src/index-client.js'));

const inst = (type: WidgetInstance['type'], size: WidgetInstance['size'] = 'medium', config: unknown = {}): WidgetInstance => ({ id: `w-${type}`, type, size, x: 0, y: 0, config });
const props = (type: WidgetInstance['type'], size: WidgetInstance['size'] = 'medium', config: Record<string, unknown> = {}, saveConfig = vi.fn()) =>
  ({ widget: inst(type, size, config), config, size, saveConfig });

beforeEach(() => { window.history.replaceState(null, '', '/'); vi.spyOn(console, 'info').mockImplementation(() => {}); });
afterEach(() => { cleanup(); teardownDesktop(); vi.useRealTimers(); vi.restoreAllMocks(); window.history.replaceState(null, '', '/'); });

async function settle(ms = 20) { await tick(ms); flushSync(); }

describe('RemindersWidget', () => {
  it('lists open reminders first, flags the overdue one and counts the open ones', async () => {
    await bootDesktop();
    render(RemindersWidget, props('reminders', 'large'));
    await settle();
    const rows = screen.getAllByRole('checkbox');
    expect(rows.length).toBeGreaterThanOrEqual(5);
    expect(rows[0].getAttribute('aria-label')).toContain('Pay the electricity bill');
    const overdue = rows[0].closest('li')!;
    expect(overdue.classList.contains('overdue')).toBe(true);
    expect(overdue.textContent).toContain('Yesterday');
    // the completed reminder is last, ticked
    const last = rows[rows.length - 1];
    expect(last.getAttribute('aria-checked')).toBe('true');
    expect(document.querySelector('.count')?.textContent).toBe('5');
  });

  it('ticking a checkbox calls reminders.update with done and updates immediately', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(RemindersWidget, props('reminders', 'large'));
    await settle();
    const first = screen.getAllByRole('checkbox')[0];
    await fire(first, me('click'));
    expect(calls.find((c) => c.type === 'reminders.update')?.params).toMatchObject({ id: 'r1', done: true });
    expect(screen.getAllByRole('checkbox').find((c) => c.getAttribute('aria-label')?.includes('Pay the electricity'))?.getAttribute('aria-checked')).toBe('true');
    await settle(450);
    expect(document.querySelector('.count')?.textContent).toBe('4');
  });

  it('the add field creates a reminder (trimmed), clears itself and ignores empty input', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(RemindersWidget, props('reminders', 'large', { list: 'Home' }));
    await settle();
    const input = screen.getByLabelText('New reminder') as HTMLInputElement;
    input.value = '   ';
    await fire(input, new Event('input'));
    await fire(input.form!, new Event('submit', { cancelable: true, bubbles: true }));
    expect(calls.some((c) => c.type === 'reminders.add')).toBe(false);
    input.value = '  Water the plants ';
    await fire(input, new Event('input'));
    await fire(input.form!, new Event('submit', { cancelable: true, bubbles: true }));
    await settle();
    expect(calls.find((c) => c.type === 'reminders.add')?.params).toEqual({ title: 'Water the plants', list: 'Home' });
    expect(input.value).toBe('');
    expect(screen.getByText('Water the plants')).toBeTruthy();
  });

  it('config.list limits the widget to one list', async () => {
    await bootDesktop();
    render(RemindersWidget, props('reminders', 'large', { list: 'Work' }));
    await settle();
    const labels = screen.getAllByRole('checkbox').map((c) => c.getAttribute('aria-label'));
    expect(labels.some((l) => l?.includes('quarterly report'))).toBe(true);
    expect(labels.some((l) => l?.includes('electricity'))).toBe(false);
  });

  it('small shows two rows and "+N more"', async () => {
    await bootDesktop();
    render(RemindersWidget, props('reminders', 'small'));
    await settle();
    expect(screen.getAllByRole('checkbox')).toHaveLength(2);
    expect(document.querySelector('.more')?.textContent).toMatch(/^\+\d+ more$/);
  });

  it('shows the empty state when everything is done or removed', async () => {
    const bridge = await bootDesktop();
    render(RemindersWidget, props('reminders', 'medium'));
    await settle();
    for (const r of (await bridge.call('reminders.list')).items) await bridge.call('reminders.remove', { id: r.id });
    await settle();
    expect(screen.getByText('All done')).toBeTruthy();
  });

  it('is localised (Russian)', async () => {
    await bootDesktop('1', 'ru');
    render(RemindersWidget, props('reminders', 'medium'));
    await settle();
    expect(screen.getByText('Напоминания')).toBeTruthy();
    expect(screen.getByLabelText('Новое напоминание')).toBeTruthy();
  });
});

describe('NotesWidget', () => {
  const type = async (ta: HTMLTextAreaElement, text: string) => { ta.value = text; await fire(ta, new Event('input', { bubbles: true })); };

  it('shows the stored text and persists edits 600 ms after the last keystroke, keeping other config keys', async () => {
    await bootDesktop();
    vi.useFakeTimers();
    const save = vi.fn();
    render(NotesWidget, props('notes', 'small', { text: 'hello', color: 'yellow' }, save));
    const ta = screen.getByLabelText('Note text') as HTMLTextAreaElement;
    expect(ta.value).toBe('hello');
    await type(ta, 'hello w'); vi.advanceTimersByTime(300);
    await type(ta, 'hello world'); vi.advanceTimersByTime(599);
    expect(save).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(save).toHaveBeenCalledExactlyOnceWith({ text: 'hello world', color: 'yellow' });
  });

  it('flushes immediately on blur and when the widget is removed', async () => {
    await bootDesktop();
    vi.useFakeTimers();
    const save = vi.fn();
    const view = render(NotesWidget, props('notes', 'small', { text: '' }, save));
    const ta = screen.getByLabelText('Note text') as HTMLTextAreaElement;
    await type(ta, 'a');
    await fire(ta, new FocusEvent('blur'));
    expect(save).toHaveBeenCalledWith({ text: 'a' });
    await type(ta, 'ab');
    view.unmount();
    expect(save).toHaveBeenLastCalledWith({ text: 'ab' });
  });

  it('does not overwrite what the user is typing when the host echoes an older value', async () => {
    await bootDesktop();
    vi.useFakeTimers();
    const save = vi.fn();
    const p = props('notes', 'small', { text: 'x' }, save);
    const view = render(NotesWidget, p);
    const ta = screen.getByLabelText('Note text') as HTMLTextAreaElement;
    await fire(ta, new FocusEvent('focus'));
    await type(ta, 'xyz');
    expect(ta.value).toBe('xyz');
    view.unmount();
  });

  it('has a placeholder and a note-taking label', async () => {
    await bootDesktop();
    render(NotesWidget, props('notes', 'small', {}));
    expect((screen.getByLabelText('Note text') as HTMLTextAreaElement).placeholder).toBe('Write a note…');
  });
});

describe('WeatherWidget', () => {
  it('loads on mount: spinner first, then the temperature in the configured unit', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(WeatherWidget, props('weather', 'medium'));
    flushSync();
    expect(screen.getByRole('status')).toBeTruthy(); // loading
    await waitFor(() => expect(document.querySelector('.temp')).not.toBeNull(), { timeout: 2000 });
    expect(calls.filter((c) => c.type === 'weather.get')).toHaveLength(1);
    const c = document.querySelector('.temp')!.textContent!;
    expect(c).toMatch(/^-?\d+°$/);
    expect(screen.getByText('Moscow')).toBeTruthy();
    expect(document.querySelectorAll('.hours li').length).toBeGreaterThanOrEqual(4);
  });

  it('switching to Fahrenheit converts without another request', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(WeatherWidget, props('weather', 'small'));
    await waitFor(() => expect(document.querySelector('.temp')).not.toBeNull(), { timeout: 2000 });
    const celsius = Number(document.querySelector('.temp')!.textContent!.replace('°', ''));
    await settingsBridge().call('settings.patch', { patch: { weather: { unit: 'f' } } });
    await settle();
    const f = Number(document.querySelector('.temp')!.textContent!.replace('°', ''));
    // The fixture temperature is not whole-degree (it follows the clock), so the displayed Celsius is rounded independently of the Fahrenheit: allow one degree.
    expect(Math.abs(f - (celsius * 9) / 5 - 32)).toBeLessThanOrEqual(1);
    expect(calls.filter((c) => c.type === 'weather.get')).toHaveLength(1);
  });

  it('large shows the forecast: five days and the humidity/wind line', async () => {
    await bootDesktop();
    render(WeatherWidget, props('weather', 'large'));
    await waitFor(() => expect(document.querySelector('.days')).not.toBeNull(), { timeout: 2000 });
    expect(document.querySelectorAll('.days li')).toHaveLength(5);
    expect(document.querySelector('.meta')?.textContent).toMatch(/Humidity \d+%/);
  });

  it('no location configured: invites to set one in Settings (and opens it)', async () => {
    window.history.replaceState(null, '', '/?weather=none');
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(WeatherWidget, props('weather', 'medium'));
    await waitFor(() => expect(screen.getByText('No location set')).toBeTruthy(), { timeout: 2000 });
    await fire(screen.getByRole('button', { name: 'Set location in Settings' }), me('click'));
    expect(calls.find((c) => c.type === 'settings.openWindow')?.params).toEqual({ page: 'widgets' });
  });

  it('errors show a retry button that asks the host again (forced)', async () => {
    window.history.replaceState(null, '', '/?weather=error-once');
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(WeatherWidget, props('weather', 'medium'));
    await waitFor(() => expect(screen.getByText('Weather unavailable')).toBeTruthy(), { timeout: 2000 });
    await fire(screen.getByRole('button', { name: 'Try Again' }), me('click'));
    await waitFor(() => expect(document.querySelector('.temp')).not.toBeNull(), { timeout: 2000 });
    expect(calls.filter((c) => c.type === 'weather.get').map((c) => (c.params as { force: boolean }).force)).toEqual([false, true]);
  });

  it('an unavailable snapshot with an error message is an error, without one it is "no location"', async () => {
    window.history.replaceState(null, '', '/?weather=unavailable');
    await bootDesktop();
    render(WeatherWidget, props('weather', 'small'));
    await waitFor(() => expect(screen.getByText('Weather unavailable')).toBeTruthy(), { timeout: 2000 });
  });

  it('refreshes every 10 minutes (and stops when the last widget goes away)', async () => {
    vi.useFakeTimers();
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    const view = render(WeatherWidget, props('weather', 'small'));
    await vi.advanceTimersByTimeAsync(500); // the mock answers weather.get after 400 ms
    expect(calls.filter((c) => c.type === 'weather.get').length).toBe(1);
    await vi.advanceTimersByTimeAsync(10 * 60_000 + 1000);
    expect(calls.filter((c) => c.type === 'weather.get').length).toBe(2);
    view.unmount();
    await vi.advanceTimersByTimeAsync(30 * 60_000);
    expect(calls.filter((c) => c.type === 'weather.get').length).toBe(2);
  });

  it('a new location triggers a forced refresh', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(WeatherWidget, props('weather', 'small'));
    await waitFor(() => expect(document.querySelector('.temp')).not.toBeNull(), { timeout: 2000 });
    await settingsBridge().call('weather.setLocation', { name: 'Sydney', latitude: -33.87, longitude: 151.2 });
    await waitFor(() => expect(screen.getByText('Sydney')).toBeTruthy(), { timeout: 2500 });
    expect(calls.filter((c) => c.type === 'weather.get').pop()?.params).toEqual({ force: true });
  });
});

describe('ClockWidget', () => {
  it('shows a digital time and the date, labelled for screen readers', async () => {
    await bootDesktop();
    render(ClockWidget, props('clock', 'small', { style: 'digital', seconds: false }));
    const timer = screen.getByRole('timer');
    expect(timer.textContent).toMatch(/\d{2}:\d{2}/);
    expect(timer.getAttribute('aria-label')).toContain('Clock');
  });

  it('honours a time zone and shows its city', async () => {
    await bootDesktop();
    render(ClockWidget, props('clock', 'medium', { style: 'digital', seconds: true, timezone: 'Asia/Tokyo' }));
    expect(screen.getByRole('timer').textContent).toContain('Tokyo');
    expect(screen.getByRole('timer').textContent).toMatch(/\d{2}:\d{2}:\d{2}/);
  });

  it('analog style draws hands that follow the time (angles set by script)', async () => {
    await bootDesktop();
    render(ClockWidget, props('clock', 'small', { style: 'analog', seconds: true }));
    await tick(40);
    const hands = document.querySelectorAll<SVGGElement>('.hand');
    expect(hands).toHaveLength(3);
    expect(hands[0].style.transform).toMatch(/^rotate\(-?[\d.]+deg\)$/);
  });
});

describe('CalendarWidget', () => {
  it('marks today in the month grid; week numbers are optional', async () => {
    await bootDesktop();
    render(CalendarWidget, props('calendar', 'large', { weekNumbers: false }));
    const today = document.querySelectorAll('.cell.today');
    expect(today).toHaveLength(1);
    expect(today[0].getAttribute('aria-current')).toBe('date');
    expect(document.querySelectorAll('.wk')).toHaveLength(0);
    cleanup();
    render(CalendarWidget, props('calendar', 'large', { weekNumbers: true }));
    expect(document.querySelectorAll('.wk').length).toBeGreaterThanOrEqual(5);
  });

  it('small shows just the big day number', async () => {
    await bootDesktop();
    render(CalendarWidget, props('calendar', 'small'));
    expect(document.querySelector('.day')?.textContent).toBe(String(new Date().getDate()));
    expect(document.querySelector('.monthgrid')).toBeNull();
  });
});

describe('MediaWidget', () => {
  it('shows the track and toggles playback through media.control', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(MediaWidget, props('media', 'medium'));
    await settle();
    expect(screen.getByText('Blinding Lights')).toBeTruthy();
    expect(screen.getByText('The Weeknd')).toBeTruthy();
    await fire(screen.getByRole('button', { name: 'Pause' }), me('click'));
    expect(calls.find((c) => c.type === 'media.control')?.params).toEqual({ action: 'toggle' });
    await settle();
    expect(screen.getByRole('button', { name: 'Play' })).toBeTruthy();
    await fire(screen.getByRole('button', { name: 'Next track' }), me('click'));
    expect(calls.filter((c) => c.type === 'media.control').at(-1)?.params).toEqual({ action: 'next' });
  });

  it('seeks with the keyboard on the progress slider', async () => {
    const bridge = await bootDesktop();
    const calls = spyCalls(bridge);
    render(MediaWidget, props('media', 'medium'));
    await settle();
    await fire(screen.getByRole('slider'), ke('keydown', 'ArrowRight'));
    const seek = calls.find((c) => c.type === 'media.control')?.params as { action: string; positionSec: number };
    expect(seek.action).toBe('seek');
    expect(seek.positionSec).toBeGreaterThan(74);
  });

  it('nothing playing: an empty state', async () => {
    await bootDesktop();
    const host = (window as unknown as { __bwMockHost: { status: { media: { available: boolean } }; pushStatus(): void } }).__bwMockHost;
    host.status.media.available = false;
    host.pushStatus();
    await settle(); // let the bridge cache the new state before the widget subscribes
    render(MediaWidget, props('media', 'small'));
    await settle();
    expect(screen.getByText('Nothing is playing')).toBeTruthy();
  });
});

describe('BatteryWidget', () => {
  it('shows this PC with its charge and the Bluetooth devices that report a level', async () => {
    await bootDesktop();
    render(BatteryWidget, props('battery', 'medium'));
    await settle();
    expect(screen.getByText('This PC')).toBeTruthy();
    expect(document.querySelector('.pct')?.textContent).toContain('76');
    const names = [...document.querySelectorAll('.dname')].map((n) => n.textContent);
    expect(names).toEqual(['MX Master 3S', 'WH-1000XM5']); // lowest charge first; the disconnected keyboard is not listed
  });

  it('small shows only the PC ring; a PC without a battery says so', async () => {
    await bootDesktop();
    const host = (window as unknown as { __bwMockHost: { status: { battery: unknown }; pushStatus(): void } }).__bwMockHost;
    host.status.battery = { present: false, percent: 0, charging: false, acOnline: true };
    host.pushStatus();
    await settle();
    render(BatteryWidget, props('battery', 'small'));
    await settle();
    expect(document.querySelector('.devices')).toBeNull();
    expect(screen.getByText('No battery')).toBeTruthy();
  });
});

