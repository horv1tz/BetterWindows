/**
 * Optimistic settings model (framework independent, unit-tested).
 *
 *   view = confirmed (last state known from the host) + every still-pending patch, in order.
 *
 * - `begin(patch)`   a control changed: the view shows the new value immediately.
 * - `ack(seq, s)`    the host answered settings.patch with the stored (validated/clamped) settings: they become the confirmed state.
 * - `fail(seq)`      the host rejected the patch: it simply disappears from the view (rollback), other pending patches stay.
 * - `external(s)`    settings.changed: somebody else (file edit, another surface) changed the settings.
 * Results of older patches that arrive after a newer one was already acknowledged are ignored (the newer result already contains them).
 */
import { defaultSettings, type Settings } from '$lib/protocol/generated';
import { applyPatch, clone } from './patch';

export class OptimisticSettings {
  private confirmed: Settings;
  private pending: Array<{ seq: number; patch: unknown }> = [];
  private nextSeq = 1;
  private lastAcked = 0;
  private cache: Settings | null = null;

  constructor(initial: Settings, private readonly defaults: Settings = defaultSettings()) {
    this.confirmed = clone(initial);
  }

  get view(): Settings {
    if (!this.cache) {
      let s = this.confirmed;
      for (const p of this.pending) s = applyPatch(s, p.patch, this.defaults);
      this.cache = s === this.confirmed ? clone(s) : s;
    }
    return this.cache;
  }

  get pendingCount(): number { return this.pending.length; }
  get confirmedState(): Settings { return this.confirmed; }

  begin(patch: unknown): number {
    const seq = this.nextSeq++;
    this.pending.push({ seq, patch: clone(patch) });
    this.cache = null;
    return seq;
  }

  ack(seq: number, stored?: Settings): void {
    this.pending = this.pending.filter((p) => p.seq !== seq);
    if (stored && seq > this.lastAcked) {
      this.confirmed = clone(stored);
      this.lastAcked = seq;
    }
    this.cache = null;
  }

  fail(seq: number): void {
    this.pending = this.pending.filter((p) => p.seq !== seq);
    this.cache = null;
  }

  external(s: Settings): void {
    this.confirmed = clone(s);
    this.cache = null;
  }
}
