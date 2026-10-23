import { getBridge } from '$lib/ipc';
import type { AppEntry } from '$lib/protocol/generated';

/** Installed apps (apps.list) — only used to show names/icons of pinned items. Loaded once, kept fresh through apps.changed. */
class AppsState {
  list = $state.raw<AppEntry[]>([]);
  byId = $derived(new Map(this.list.map((a) => [a.id, a])));
  loaded = false;
}
export const appsState = new AppsState();

export async function loadApps(): Promise<void> {
  if (appsState.loaded) return;
  appsState.loaded = true;
  try {
    appsState.list = (await getBridge().call('apps.list')).apps;
  } catch {
    appsState.loaded = false; // retry next time the page opens; names fall back to the pin's own data
  }
}
