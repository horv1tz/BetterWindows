import { BridgeError } from '../../lib/ipc/bridge';
import { appById, appByName } from '../fixtures';
import type { MockContext, MockHost } from '../host';

/**
 * Menu bar helpers for the mock host. The frozen domains already feed the bar (activeApp.changed, status.*, media.state, tray.state,
 * overlay.open); this file adds only what they lack:
 *
 *  1. overlay.show / overlay.hide also reach the menu bar surfaces, so the pressed state of an item can be seen in the mock.
 *     (In the real host the overlay controller decides who receives them; the bar also copes when it receives neither.)
 *  2. dock.hide / dock.quit for the app menu popover, unless the dock mock registered them already (dock.ts loads before this file).
 *  3. `?demo=1` (or `?demo=<seconds>`): cycles the active app every few seconds to show the name change animation.
 */
const RUNNING = ['File Explorer', 'Microsoft Edge', 'Visual Studio Code', 'Terminal', 'Spotify'];

export default function register(host: MockHost) {
  const q = new URLSearchParams(location.search);
  const h = host as unknown as {
    emit: MockHost['emit'];
    calls: Map<string, unknown>;
    setActiveApp?: (appId: string | null) => void;
    getActiveApp?: () => string | null;
  };

  // 1. fan overlay.show / overlay.hide out to the menu bars as well
  const baseEmit = host.emit.bind(host) as (type: string, payload: unknown, filter?: (c: MockContext) => boolean) => void;
  h.emit = ((type: string, payload: unknown, filter?: (c: MockContext) => boolean) => {
    baseEmit(type, payload, filter);
    if (type === 'overlay.show' || type === 'overlay.hide') {
      baseEmit(type, payload, (c) => c.surface === 'menubar' && !(filter ? filter(c) : true));
    }
  }) as MockHost['emit'];

  // 2. app menu calls
  const running = new Set(RUNNING.map((n) => appByName(n).id));
  const next = (after: string | null): string | null => {
    const ids = [...running].filter((id) => id !== after);
    return ids[0] ?? null;
  };
  const validate = (itemId: unknown): string => {
    if (typeof itemId !== 'string' || !itemId || itemId.length > 1024) throw new BridgeError('badRequest', 'itemId is required');
    if (!running.has(itemId) && !appById(itemId)) throw new BridgeError('notFound', `unknown app '${itemId}'`);
    return itemId;
  };
  if (!h.calls.has('dock.hide')) {
    host.handle('dock.hide', (p) => {
      const id = validate(p.itemId);
      console.info('[mock] dock.hide', id);
      if (h.getActiveApp?.() === id) h.setActiveApp?.(next(id));
    });
  }
  if (!h.calls.has('dock.quit')) {
    host.handle('dock.quit', (p) => {
      const id = validate(p.itemId);
      console.info('[mock] dock.quit', id);
      running.delete(id);
      if (h.getActiveApp?.() === id) h.setActiveApp?.(next(id));
    });
  }

  // 3. demo mode
  const demo = q.get('demo');
  if (demo !== null) {
    const seconds = Math.max(2, Number(demo) > 1 ? Number(demo) : 6);
    let started = false;
    host.onSurfaceReady((ctx) => {
      if (ctx.surface !== 'menubar' || started) return;
      started = true;
      let i = 0;
      setInterval(() => {
        const names = RUNNING.map((n) => appByName(n).id);
        h.setActiveApp?.(names[++i % names.length]);
      }, seconds * 1000);
    });
  }
}
