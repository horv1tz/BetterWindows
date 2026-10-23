/**
 * Reactive settings store of the settings window: an optimistic mirror of the host's settings.
 *   store.s            current settings (confirmed + pending patches)
 *   store.patch({...}) minimal merge patch; UI updates immediately, rolls back + toasts when the host rejects it
 */
import { getBridge, getInit, type Bridge } from '$lib/ipc';
import type { Settings } from '$lib/protocol/generated';
import { OptimisticSettings } from './optimistic';
import { resetAllPatch, type SettingsPatch } from './patch';
import { errorText } from './actions';
import { BridgeError } from '$lib/ipc';
import { pushToast } from './toast.svelte';
import { t } from './i18n';

export interface StoreOptions {
  /** called for a rejected patch (default: error toast) */
  onError?: (code: string, message: string) => void;
}

export class SettingsStore {
  private opt: OptimisticSettings;
  private view = $state.raw<Settings>({} as Settings);
  private off: () => void;
  private onError: (code: string, message: string) => void;

  constructor(private bridge: Bridge, initial: Settings, options: StoreOptions = {}) {
    this.opt = new OptimisticSettings(initial);
    this.view = this.opt.view;
    this.onError = options.onError ?? ((code, message) => {
      if (code === 'unsupported' || code === 'cancelled') return;
      pushToast('error', `${t('toast.saveFailed')} ${message}`);
    });
    this.off = bridge.on('settings.changed', (s) => { this.opt.external(s); this.sync(); });
  }

  /** Current settings (reactive). */
  get s(): Settings { return this.view; }
  get pending(): number { return this.opt.pendingCount; }

  private sync() { this.view = this.opt.view; }

  /** Optimistic settings.patch; resolves true when the host accepted it. */
  async patch(patch: SettingsPatch): Promise<boolean> {
    const seq = this.opt.begin(patch);
    this.sync();
    try {
      const stored = await this.bridge.call('settings.patch', { patch });
      this.opt.ack(seq, stored);
      this.sync();
      return true;
    } catch (e) {
      this.opt.fail(seq);
      this.sync();
      const code = e instanceof BridgeError ? e.code : 'internal';
      this.onError(code, errorText(e));
      return false;
    }
  }

  /** "Reset all settings": minimal patch to the generated defaults (keeps firstRunDone). */
  resetAll(): Promise<boolean> {
    return this.patch(resetAllPatch(this.opt.view));
  }

  dispose() { this.off(); }
}

let instance: SettingsStore | null = null;

/** The window's store (created on first use from SurfaceInit.settings). */
export function getStore(): SettingsStore {
  return (instance ??= new SettingsStore(getBridge(), getInit().settings));
}
export function resetStoreForTests() { instance?.dispose(); instance = null; }
