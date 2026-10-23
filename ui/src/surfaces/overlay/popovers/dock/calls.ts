import { BridgeError, getBridge } from '$lib/ipc';

/** Fire a host call from a popover: failures are logged (never thrown into the UI); `unsupported` is silent (docs/IPC.md section 9). */
export async function fire(p: Promise<unknown>, what: string): Promise<boolean> {
  try { await p; return true; } catch (e) {
    if (!(e instanceof BridgeError && e.code === 'unsupported')) {
      getBridge().emit('surface.log', { level: 'warn', message: `${what} failed: ${e instanceof Error ? e.message : String(e)}` });
    }
    return false;
  }
}
