import { BridgeError } from '$lib/ipc';
import { pushToast } from './toast.svelte';
import { t } from './i18n';

/** Message shown to the user for a failed call (the host's text is English; our prefix is localised). */
export function errorText(e: unknown): string {
  if (e instanceof BridgeError) return e.message || e.code;
  if (e instanceof Error) return e.message;
  return String(e);
}

export type RunResult<T> = { ok: true; value: T } | { ok: false; code: string; message: string };

/**
 * Run a bridge call and turn failures into a result (and, unless `quiet`, an error toast).
 * `unsupported` and `cancelled` never toast: docs/IPC.md section 9 says the UI degrades the affected control instead.
 */
export async function run<T>(fn: () => Promise<T>, opts: { quiet?: boolean; what?: string } = {}): Promise<RunResult<T>> {
  try {
    return { ok: true, value: await fn() };
  } catch (e) {
    const code = e instanceof BridgeError ? e.code : 'internal';
    const message = errorText(e);
    if (!opts.quiet && code !== 'unsupported' && code !== 'cancelled') {
      pushToast('error', opts.what ? `${opts.what}: ${message}` : message);
    }
    return { ok: false, code, message };
  }
}

/** Short human text for the most common failure codes, falling back to the host message. */
export function friendlyError(code: string, message: string): string {
  switch (code) {
    case 'notFound': return message || t('err.notFound');
    case 'unsupported': return t('err.unsupported');
    case 'busy': return t('err.busy');
    case 'forbidden': return t('err.forbidden');
    case 'timeout': return t('err.timeout');
    default: return message || t('err.generic');
  }
}
