// Reactive helpers shared by the desktop surface and its widgets.
import { onDestroy } from 'svelte';
import { getBridge, getInit } from '$lib/ipc';
import { hostState } from '$lib/ipc/state.svelte';
import { currentLocale } from '$lib/i18n.svelte';
import type { HostEventType, HostEvents, Settings } from '$lib/protocol/generated';

let settingsMirror: ReturnType<typeof hostState<'settings.changed'>> | null = null;

/** The live settings (SurfaceInit.settings first, then every settings.changed). One subscription for the lifetime of the page. */
export function liveSettings(): { readonly value: Settings } {
  return (settingsMirror ??= hostState('settings.changed', getInit().settings));
}

export function resetShared() { settingsMirror?.dispose(); settingsMirror = null; }

/** hostState() that unsubscribes when the calling component is destroyed. */
export function useHostState<K extends HostEventType>(type: K, initial: HostEvents[K]) {
  const s = hostState(type, initial);
  onDestroy(() => s.dispose());
  return s;
}

/** 12/24 h clock preference (shared with the menu bar). */
export function use24Hour(): boolean { return liveSettings().value.menuBar.use24Hour; }

export function locale(): string { return currentLocale(); }

/** Fire-and-forget bridge call that logs failures instead of throwing into event handlers. */
export function callQuiet<T>(p: Promise<T>, what: string): void {
  p.catch((e) => getBridge().emit('surface.log', { level: 'warn', message: `${what} failed: ${e instanceof Error ? e.message : String(e)}` }));
}
