import { describe, expect, it } from 'vitest';
import { MENU, confirmKeys, errorKindOf, needsConfirmation, reduceSheet, type SheetState } from './confirm';

describe('needsConfirmation', () => {
  it('requires confirmation for restart, shutdown and logout only', () => {
    expect(needsConfirmation('restart')).toBe(true);
    expect(needsConfirmation('shutdown')).toBe(true);
    expect(needsConfirmation('logout')).toBe(true);
    expect(needsConfirmation('sleep')).toBe(false);
    expect(needsConfirmation('lock')).toBe(false);
    expect(needsConfirmation('hibernate')).toBe(false);
  });
});

describe('reduceSheet', () => {
  const confirm = (o: Partial<Extract<SheetState, { phase: 'confirm' }>> = {}): SheetState => ({ phase: 'confirm', action: 'restart', busy: false, error: null, ...o });

  it('asking for a destructive action opens the sheet for exactly that action', () => {
    expect(reduceSheet(MENU, { type: 'ask', action: 'shutdown' })).toEqual(confirm({ action: 'shutdown' }));
  });
  it('asking for a harmless action does not open the sheet', () => {
    expect(reduceSheet(MENU, { type: 'ask', action: 'sleep' })).toBe(MENU);
    expect(reduceSheet(MENU, { type: 'ask', action: 'lock' })).toBe(MENU);
  });
  it('a second ask while a sheet is open is ignored (cannot swap the action under the user)', () => {
    const s = confirm({ action: 'restart' });
    expect(reduceSheet(s, { type: 'ask', action: 'shutdown' })).toBe(s);
  });
  it('cancel returns to the menu', () => {
    expect(reduceSheet(confirm(), { type: 'cancel' })).toBe(MENU);
    expect(reduceSheet(confirm({ error: 'failed' }), { type: 'cancel' })).toBe(MENU);
  });
  it('submit marks the sheet busy and clears an earlier error', () => {
    expect(reduceSheet(confirm({ error: 'failed' }), { type: 'submit' })).toEqual(confirm({ busy: true, error: null }));
  });
  it('a double submit is ignored while busy, and cancel cannot interrupt a request in flight', () => {
    const busy = confirm({ busy: true });
    expect(reduceSheet(busy, { type: 'submit' })).toBe(busy);
    expect(reduceSheet(busy, { type: 'cancel' })).toBe(busy);
  });
  it('a failure re-enables the buttons and keeps the sheet open with the error, so it can be retried', () => {
    const failed = reduceSheet(confirm({ busy: true }), { type: 'failed', error: 'unsupported' });
    expect(failed).toEqual(confirm({ busy: false, error: 'unsupported' }));
    expect(reduceSheet(failed, { type: 'submit' })).toEqual(confirm({ busy: true, error: null }));
  });
  it('a stray failure with no request in flight is ignored', () => {
    const s = confirm();
    expect(reduceSheet(s, { type: 'failed', error: 'failed' })).toBe(s);
    expect(reduceSheet(MENU, { type: 'failed', error: 'failed' })).toBe(MENU);
    expect(reduceSheet(MENU, { type: 'submit' })).toBe(MENU);
    expect(reduceSheet(MENU, { type: 'cancel' })).toBe(MENU);
  });
});

describe('errorKindOf / confirmKeys', () => {
  it('distinguishes unsupported from every other failure', () => {
    expect(errorKindOf({ code: 'unsupported' })).toBe('unsupported');
    expect(errorKindOf({ code: 'failed' })).toBe('failed');
    expect(errorKindOf(new Error('boom'))).toBe('failed');
    expect(errorKindOf(null)).toBe('failed');
  });
  it('builds the dictionary keys of an action', () => {
    expect(confirmKeys('logout')).toEqual({ title: 'confirm.logout.title', body: 'confirm.logout.body', action: 'confirm.logout.action', busy: 'confirm.logout.busy' });
  });
});
