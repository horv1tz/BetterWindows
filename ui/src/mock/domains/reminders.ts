// Mock of the reminders service: CRUD, reminders.changed (state, pushed to the desktop and overlay surfaces on ready) and a simulated
// "due" event. Owner: ui-desktop. Query flag: ?reminders=empty starts without items.
import { BridgeError } from '../../lib/ipc/bridge';
import type { Reminder, ReminderList } from '../../lib/protocol/generated';
import type { MockContext, MockHost } from '../host';
import { query } from './widgets';

const pad = (n: number) => String(n).padStart(2, '0');
/** ISO-8601 local date-time without offset (what the host sends). */
export const isoLocal = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}:00`;

/** Seed relative to `now` so the widget always shows an overdue item, today/tomorrow items and a completed one. */
export function seedReminders(now: Date): Reminder[] {
  const at = (dayOffset: number, h: number, m = 0) => isoLocal(new Date(now.getFullYear(), now.getMonth(), now.getDate() + dayOffset, h, m));
  return [
    { id: 'r1', title: 'Pay the electricity bill', due: at(-1, 18), done: false, list: 'Home' },
    { id: 'r2', title: 'Call Anna about the trip', due: at(0, Math.min(23, Math.max(now.getHours() + 3, 17)), 30), done: false, list: 'Personal' },
    { id: 'r3', title: 'Dentist appointment', due: at(1, 9, 30), done: false, notes: 'Bring the insurance card', list: 'Personal' },
    { id: 'r4', title: 'Review the quarterly report', due: at(2, 14), done: false, list: 'Work' },
    { id: 'r5', title: 'Buy groceries', done: false, list: 'Home' },
    { id: 'r6', title: 'Send the invoice', due: at(-2, 10), done: true, list: 'Work' },
  ];
}

/** Applies the protocol's update semantics: absent = unchanged, empty string clears due/notes/list. Throws BridgeError for bad input. */
export function applyReminderUpdate(item: Reminder, p: { title?: string; due?: string; notes?: string; done?: boolean; list?: string }): Reminder {
  const next: Reminder = { ...item };
  if (p.title !== undefined) {
    const title = p.title.trim();
    if (!title) throw new BridgeError('badRequest', 'title must not be empty');
    if (title.length > 500) throw new BridgeError('badRequest', 'title is too long');
    next.title = title;
  }
  if (p.due !== undefined) {
    if (p.due === '') delete next.due;
    else if (Number.isNaN(new Date(p.due).getTime())) throw new BridgeError('badRequest', 'due must be an ISO-8601 date-time');
    else next.due = p.due;
  }
  if (p.notes !== undefined) { if (p.notes === '') delete next.notes; else next.notes = p.notes.slice(0, 4000); }
  if (p.list !== undefined) { if (p.list === '') delete next.list; else next.list = p.list.slice(0, 100); }
  if (p.done !== undefined) next.done = !!p.done;
  return next;
}

export default function register(host: MockHost) {
  const q = query();
  let items: Reminder[] = q.get('reminders') === 'empty' ? [] : seedReminders(new Date());
  let seq = 100;
  const fired = new Set<string>();
  let timer: ReturnType<typeof setInterval> | null = null;

  const list = (): ReminderList => ({ items: structuredClone(items) });
  const publish = () => host.emit('reminders.changed', list());

  /** Simulated scheduler: fires reminders.due once when an open reminder's time passes (also the one added by "?reminders=soon"). */
  function startTimer() {
    if (timer || typeof setInterval === 'undefined') return;
    timer = setInterval(() => {
      const now = Date.now();
      for (const r of items) {
        if (r.done || !r.due || fired.has(r.id)) continue;
        const t = new Date(r.due).getTime();
        if (t <= now && now - t < 60_000) { fired.add(r.id); host.emit('reminders.due', structuredClone(r)); }
        else if (t <= now) fired.add(r.id); // long overdue at load: do not fire retroactively
      }
    }, 1000);
  }

  host.onSurfaceReady((ctx: MockContext) => {
    if (ctx.surface !== 'desktop' && ctx.surface !== 'overlay') return;
    if (q.get('reminders') === 'soon' && !items.some((r) => r.id === 'soon')) {
      items = [...items, { id: 'soon', title: 'Stand up and stretch', due: isoLocal(new Date(Date.now() + 2 * 60_000)), done: false }];
    }
    host.emitTo(ctx, 'reminders.changed', list());
    startTimer();
  });

  host.handle('reminders.list', () => list());

  host.handle('reminders.add', (p) => {
    const title = typeof p.title === 'string' ? p.title.trim() : '';
    if (!title) throw new BridgeError('badRequest', 'title is required');
    if (title.length > 500) throw new BridgeError('badRequest', 'title is too long');
    if (p.due && Number.isNaN(new Date(p.due).getTime())) throw new BridgeError('badRequest', 'due must be an ISO-8601 date-time');
    const r: Reminder = { id: `r${++seq}`, title, done: false, ...(p.due ? { due: p.due } : {}), ...(p.notes ? { notes: p.notes.slice(0, 4000) } : {}), ...(p.list ? { list: p.list.slice(0, 100) } : {}) };
    items = [...items, r];
    publish();
    return structuredClone(r);
  });

  host.handle('reminders.update', (p) => {
    const i = items.findIndex((r) => r.id === p.id);
    if (i < 0) throw new BridgeError('notFound', `no reminder '${p.id}'`);
    const next = applyReminderUpdate(items[i], p);
    items = items.map((r, k) => (k === i ? next : r));
    if (p.due !== undefined) fired.delete(next.id);
    publish();
    return structuredClone(next);
  });

  host.handle('reminders.remove', (p) => {
    if (!items.some((r) => r.id === p.id)) throw new BridgeError('notFound', `no reminder '${p.id}'`);
    items = items.filter((r) => r.id !== p.id);
    publish();
  });
}
