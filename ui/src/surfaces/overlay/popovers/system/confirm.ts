import type { PowerAction } from '$lib/protocol/generated';

/**
 * Logic of the in-popover confirmation sheet for destructive power actions.
 * Pure (no DOM, no bridge) so the state machine can be unit tested; PowerConfirm.svelte renders it.
 */
export type ConfirmAction = 'restart' | 'shutdown' | 'logout';

/** Restart, shut down and log out end the session and can lose unsaved work: they need an explicit confirmation. Sleep and lock do not. */
export function needsConfirmation(action: PowerAction): action is ConfirmAction {
  return action === 'restart' || action === 'shutdown' || action === 'logout';
}

export type SheetState =
  | { phase: 'menu' }
  | { phase: 'confirm'; action: ConfirmAction; busy: boolean; error: ErrorKind | null };

export type ErrorKind = 'failed' | 'unsupported';

export type SheetEvent =
  | { type: 'ask'; action: PowerAction }
  | { type: 'cancel' }
  | { type: 'submit' }
  | { type: 'failed'; error: ErrorKind };

export const MENU: SheetState = { phase: 'menu' };

export function reduceSheet(state: SheetState, ev: SheetEvent): SheetState {
  switch (ev.type) {
    case 'ask':
      // Only destructive actions open the sheet; the caller runs the others straight away.
      return state.phase === 'menu' && needsConfirmation(ev.action) ? { phase: 'confirm', action: ev.action, busy: false, error: null } : state;
    case 'cancel':
      // A request that is already in flight cannot be taken back.
      return state.phase === 'confirm' && !state.busy ? MENU : state;
    case 'submit':
      // Ignore a second click while the first is in flight (double click on "Restart").
      return state.phase === 'confirm' && !state.busy ? { ...state, busy: true, error: null } : state;
    case 'failed':
      return state.phase === 'confirm' && state.busy ? { ...state, busy: false, error: ev.error } : state;
  }
}

/** Maps a failed system.power call to a user-facing message kind. `unsupported` is the protocol's "not available on this machine". */
export function errorKindOf(e: unknown): ErrorKind {
  return typeof e === 'object' && e !== null && (e as { code?: unknown }).code === 'unsupported' ? 'unsupported' : 'failed';
}

/** Dictionary keys of the sheet copy for an action. */
export function confirmKeys(action: ConfirmAction) {
  return {
    title: `confirm.${action}.title`,
    body: `confirm.${action}.body`,
    action: `confirm.${action}.action`,
    busy: `confirm.${action}.busy`,
  } as const;
}
