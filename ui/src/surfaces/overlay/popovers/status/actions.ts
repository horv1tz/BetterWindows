/**
 * Host calls used by the popovers. Every call here has no result, so the helper reports success as a boolean. Failures are
 * logged, never thrown into the UI: the popover keeps showing the host state, which is the source of truth.
 */
import { getBridge } from '$lib/ipc';
import type { CallType, Calls, ThemePreference } from '$lib/protocol/generated';

/** Sends a call that carries no result. Resolves true when the host accepted it, false when it refused or failed. */
export async function send<K extends CallType>(type: K, ...args: Calls[K]['params'] extends void ? [] : [Calls[K]['params']]): Promise<boolean> {
  try {
    await (getBridge().call as (t: K, ...a: unknown[]) => Promise<unknown>)(type, ...args);
    return true;
  } catch (e) {
    console.warn(`[popover] ${type} failed`, e);
    return false;
  }
}

/** Opens a documented ms-settings: URI through the host (the host rejects anything else). */
export function openSettings(target: string): Promise<boolean> {
  return send('system.open', { target });
}

export function setThemePreference(preference: ThemePreference): Promise<boolean> {
  return send('theme.setPreference', { preference });
}
