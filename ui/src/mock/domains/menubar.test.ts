import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { MockBridge } from '../mockBridge';
import { getMockHost, resetMockHost } from '../host';
import { appByName } from '../fixtures';

vi.spyOn(console, 'info').mockImplementation(() => {});

const tick = () => new Promise((r) => setTimeout(r, 0));
const explorer = appByName('File Explorer').id;
const edge = appByName('Microsoft Edge').id;

async function surfaces() {
  const bar = new MockBridge({ surface: 'menubar', monitorId: '1' });
  const overlay = new MockBridge({ surface: 'overlay' });
  await bar.call('surface.ready', { surface: 'menubar', monitorId: '1' });
  await overlay.call('surface.ready', { surface: 'overlay' });
  await tick();
  return { bar, overlay };
}

beforeEach(() => resetMockHost());
afterEach(() => resetMockHost());

describe('mock menubar domain: overlay events reach the menu bar', () => {
  it('overlay.open produces overlay.show on the overlay AND the menu bar, overlay.close the matching overlay.hide', async () => {
    const { bar, overlay } = await surfaces();
    const seen: string[] = [];
    bar.on('overlay.show', (s) => seen.push(`bar:show:${s.popover?.kind}`));
    bar.on('overlay.hide', () => seen.push('bar:hide'));
    overlay.on('overlay.show', (s) => seen.push(`overlay:show:${s.popover?.kind}`));
    overlay.on('overlay.hide', () => seen.push('overlay:hide'));
    await bar.call('overlay.open', { mode: 'popover', popover: { kind: 'sound', anchor: { x: 1, y: 2, width: 3, height: 4 } } });
    await overlay.call('overlay.close');
    await tick();
    expect(seen.sort()).toEqual(['bar:hide', 'bar:show:sound', 'overlay:hide', 'overlay:show:sound']);
  });

  it('does not deliver twice to surfaces the original emit already reached', async () => {
    const { bar } = await surfaces();
    let n = 0;
    bar.on('theme.changed', () => n++);
    getMockHost().emit('theme.changed', getMockHost().theme); // broadcast: the wrapper must leave non-overlay events alone
    await tick();
    expect(n).toBe(1);
  });
});

describe('mock menubar domain: dock.hide / dock.quit for the app menu', () => {
  it('hide moves the foreground to another running app', async () => {
    const { bar } = await surfaces();
    const host = getMockHost() as any;
    expect(host.getActiveApp()).toBe(explorer);
    await bar.call('dock.hide', { itemId: explorer });
    expect(host.getActiveApp()).not.toBe(explorer);
    expect(host.getActiveApp()).not.toBeNull();
  });

  it('quit of the foreground app hands the foreground to another running app', async () => {
    const { bar } = await surfaces();
    const host = getMockHost() as any;
    host.setActiveApp(edge);
    await bar.call('dock.quit', { itemId: edge });
    expect(host.getActiveApp()).not.toBe(edge);
  });

  it('validates the payload like the real host: missing id is badRequest, unknown id notFound', async () => {
    const { bar } = await surfaces();
    await expect(bar.call('dock.quit', { itemId: '' })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(bar.call('dock.hide', { itemId: 'exe:c:\\nope.exe' })).rejects.toMatchObject({ code: 'notFound' });
  });
});
