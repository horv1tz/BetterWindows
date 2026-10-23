import { describe, expect, it } from 'vitest';
import { NOT_PRESSED, keyOfShow, openRequest, reducePressed, type Pressed } from './popoverTracker';

const popover = (kind: any) => ({ mode: 'popover' as const, monitorId: '1', popover: { kind, anchor: { x: 0, y: 0, width: 1, height: 1 } } });

describe('keyOfShow', () => {
  it('maps bar popovers and spotlight to their item', () => {
    expect(keyOfShow(popover('battery'))).toBe('battery');
    expect(keyOfShow(popover('apple'))).toBe('apple');
    expect(keyOfShow(popover('appMenu'))).toBe('appMenu');
    expect(keyOfShow({ mode: 'spotlight', monitorId: '1' })).toBe('spotlight');
  });
  it('ignores panels that do not come from the bar', () => {
    expect(keyOfShow(popover('dockMenu'))).toBeNull();
    expect(keyOfShow(popover('windowList'))).toBeNull();
    expect(keyOfShow(popover('desktopMenu'))).toBeNull();
    expect(keyOfShow({ mode: 'launchpad', monitorId: '1' })).toBeNull();
    expect(keyOfShow({ mode: 'missionControl', monitorId: '1' })).toBeNull();
    expect(keyOfShow({ mode: 'popover', monitorId: '1' })).toBeNull();
  });
});

describe('reducePressed', () => {
  const pressedSound: Pressed = { key: 'sound', armed: false };

  it('click presses the item optimistically', () => {
    expect(reducePressed(NOT_PRESSED, { type: 'click', key: 'sound' })).toEqual({ key: 'sound', armed: false });
  });
  it('overlay.show for the same item keeps it pressed; for another item moves the press', () => {
    expect(reducePressed(pressedSound, { type: 'show', show: popover('sound'), monitorId: '1' }).key).toBe('sound');
    expect(reducePressed(pressedSound, { type: 'show', show: popover('network'), monitorId: '1' }).key).toBe('network');
  });
  it('overlay.show of a non-bar panel or of a panel on another monitor releases the item', () => {
    expect(reducePressed(pressedSound, { type: 'show', show: popover('dockMenu'), monitorId: '1' })).toEqual(NOT_PRESSED);
    expect(reducePressed(pressedSound, { type: 'show', show: { ...popover('sound'), monitorId: '2' }, monitorId: '1' })).toEqual(NOT_PRESSED);
  });
  it('overlay.show without a known own monitor id still presses (single-monitor mock route)', () => {
    expect(reducePressed(NOT_PRESSED, { type: 'show', show: popover('calendar'), monitorId: undefined }).key).toBe('calendar');
  });
  it('overlay.show also presses an item the user did not click (hotkey opened Spotlight)', () => {
    expect(reducePressed(NOT_PRESSED, { type: 'show', show: { mode: 'spotlight', monitorId: '1' }, monitorId: '1' }).key).toBe('spotlight');
  });
  it('overlay.hide and a failed overlay.open release', () => {
    expect(reducePressed(pressedSound, { type: 'hide' })).toEqual(NOT_PRESSED);
    expect(reducePressed(pressedSound, { type: 'openFailed' })).toEqual(NOT_PRESSED);
    expect(reducePressed(NOT_PRESSED, { type: 'hide' })).toBe(NOT_PRESSED);
  });
  it('fallback: the pointer must leave first, then moving again means the panel closed', () => {
    let s = reducePressed(NOT_PRESSED, { type: 'click', key: 'media' });
    s = reducePressed(s, { type: 'pointerMove' });
    expect(s.key).toBe('media'); // moving inside the bar right after the click does not release
    s = reducePressed(s, { type: 'pointerLeave' });
    expect(s).toEqual({ key: 'media', armed: true });
    s = reducePressed(s, { type: 'pointerMove' });
    expect(s).toEqual(NOT_PRESSED);
  });
  it('pointer events do nothing when nothing is pressed', () => {
    expect(reducePressed(NOT_PRESSED, { type: 'pointerLeave' })).toBe(NOT_PRESSED);
    expect(reducePressed(NOT_PRESSED, { type: 'pointerMove' })).toBe(NOT_PRESSED);
  });
});

describe('openRequest', () => {
  const rect = { x: 10, y: 3, width: 40, height: 22 };
  it('popover items carry kind, anchor, monitor and optional data', () => {
    expect(openRequest('battery', rect, '2')).toEqual({ mode: 'popover', monitorId: '2', popover: { kind: 'battery', anchor: rect } });
    expect(openRequest('appMenu', rect, '1', { appId: 'exe:c:\\a.exe' }).popover?.data).toEqual({ appId: 'exe:c:\\a.exe' });
  });
  it('spotlight is a mode, not a popover', () => {
    expect(openRequest('spotlight', rect, '1')).toEqual({ mode: 'spotlight', monitorId: '1' });
  });
});
