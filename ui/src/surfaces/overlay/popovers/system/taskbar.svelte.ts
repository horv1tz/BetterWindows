import { getBridge, getInit } from '$lib/ipc';
import type { TaskbarMode } from '$lib/protocol/generated';

/** Whether the native Windows taskbar is visible for a given settings.taskbar.mode ('autoHide' counts as hidden until the pointer reveals it). */
export const taskbarVisibleFor = (mode: TaskbarMode): boolean => mode === 'visible';

/**
 * The "Show Windows Taskbar" check state, held here (module scope) so it survives the popover being closed and reopened.
 * system.showTaskbar is temporary and never changes settings, so the host does not report it back: this is the UI's own record.
 * It starts from settings.taskbar.mode and resets whenever that setting changes (the Core controller then re-applies the mode).
 */
let visible = $state(false);
let lastMode: TaskbarMode | null = null;
let started = false;

function start() {
  if (started) return;
  started = true;
  const settings = getInit().settings;
  lastMode = settings.taskbar.mode;
  visible = taskbarVisibleFor(lastMode);
  getBridge().on('settings.changed', (s) => {
    if (s.taskbar.mode === lastMode) return;
    lastMode = s.taskbar.mode;
    visible = taskbarVisibleFor(lastMode);
  });
}

export const taskbar = {
  /** Seed from settings and subscribe to changes. Call once while a component initialises (not from a $derived: it writes state). */
  init: start,
  get visible(): boolean {
    return visible;
  },
  /** Flip the taskbar. Optimistic; rolls back and rethrows if the host refuses. */
  async toggle(): Promise<void> {
    start();
    const next = !visible;
    visible = next;
    try {
      await getBridge().call('system.showTaskbar', { show: next });
    } catch (e) {
      visible = !next;
      throw e;
    }
  },
};

/** Test helper: forget cached state so a new bridge/init is picked up. */
export function resetTaskbarForTests() {
  started = false;
  visible = false;
  lastMode = null;
}
