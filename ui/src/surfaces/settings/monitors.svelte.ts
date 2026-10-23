import { getBridge, getInit } from '$lib/ipc';
import type { MonitorInfo } from '$lib/protocol/generated';
import { sortMonitors } from './format';

class Monitors {
  list = $state.raw<MonitorInfo[]>([]);
  loaded = $state(false);

  get primaryId(): string { return (this.list.find((m) => m.primary) ?? this.list[0])?.id ?? ''; }
  get multiple(): boolean { return this.list.length > 1; }
  find(id: string | undefined | null): MonitorInfo | undefined { return this.list.find((m) => m.id === (id ?? this.primaryId)); }

  /** Loads system.monitors once (or again with force); falls back to the monitor of this window when the host cannot enumerate. */
  async load(force = false): Promise<void> {
    if (this.loaded && !force) return;
    try {
      const r = await getBridge().call('system.monitors');
      this.list = sortMonitors(r.monitors);
    } catch {
      try { this.list = [getInit().monitor]; } catch { this.list = []; }
    }
    if (this.list.length === 0) { try { this.list = [getInit().monitor]; } catch { /* no init in tests */ } }
    this.loaded = true;
  }
}

export const monitors = new Monitors();
