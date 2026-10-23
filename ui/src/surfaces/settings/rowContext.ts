import { getContext, setContext } from 'svelte';

/** A Row publishes the ids of its label/description so the control inside can reference them (aria-labelledby / aria-describedby). */
export interface RowContext { labelId: string; descId?: string; disabled: boolean }
const KEY = Symbol('settings-row');

export const setRowContext = (ctx: () => RowContext): void => {
  setContext(KEY, ctx);
};
export const getRowContext = (): (() => RowContext) | undefined => getContext<(() => RowContext) | undefined>(KEY);

let uid = 0;
export const nextId = (prefix: string): string => `${prefix}-${++uid}`;
