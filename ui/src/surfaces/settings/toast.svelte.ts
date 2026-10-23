export type ToastKind = 'error' | 'info' | 'success';
export interface Toast { id: number; kind: ToastKind; text: string }

export const toasts = $state<{ list: Toast[] }>({ list: [] });
let seq = 1;
const timers = new Map<number, ReturnType<typeof setTimeout>>();

/** Show a transient message (most recent last, at most 3 visible). */
export function pushToast(kind: ToastKind, text: string, ms = kind === 'error' ? 6000 : 3500): number {
  const id = seq++;
  toasts.list = [...toasts.list, { id, kind, text }].slice(-3);
  timers.set(id, setTimeout(() => dismissToast(id), ms));
  return id;
}

export function dismissToast(id: number): void {
  const t = timers.get(id);
  if (t) clearTimeout(t);
  timers.delete(id);
  toasts.list = toasts.list.filter((x) => x.id !== id);
}

export function clearToasts(): void {
  for (const id of [...timers.keys()]) dismissToast(id);
  toasts.list = [];
}
