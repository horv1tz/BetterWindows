import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
// Lives here, not next to the domain: mock/host.ts globs ./domains/*.ts (test files included) and would execute it inside every other test file.
import { MockBridge } from '$mock/mockBridge';
import { getMockHost, resetMockHost } from '$mock/host';
import { appByName } from '$mock/fixtures';
import type { OverlayRequest } from '$lib/protocol/generated';

vi.spyOn(console, 'info').mockImplementation(() => {});

const tick = () => new Promise((r) => setTimeout(r, 0));
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
    let token = -1;
    bar.on('overlay.show', (s) => seen.push(`bar:show:${s.popover?.kind}`));
    bar.on('overlay.hide', () => seen.push('bar:hide'));
    overlay.on('overlay.show', (s) => { token = s.token; seen.push(`overlay:show:${s.popover?.kind}`); });
    overlay.on('overlay.hide', () => seen.push('overlay:hide'));
    const req: OverlayRequest = { mode: 'popover', popover: { kind: 'sound', anchor: { x: 1, y: 2, width: 3, height: 4 } } };
    await bar.call('overlay.open', req);
    await tick();
    // overlay.close is token-guarded by the protocol: the token of the show being closed
    await overlay.call('overlay.close', { token });
    await tick();
    expect(seen.sort()).toEqual(['bar:hide', 'bar:show:sound', 'overlay:hide', 'overlay:show:sound']);
  });

  it('does not deliver twice to surfaces the original emit already reached', async () => {
    const { bar } = await surfaces();
    let n = 0;
    bar.on('theme.changed', () => n++);
    await tick(); // a new subscriber gets the cached state event once (replay); count only the live broadcast below
    n = 0;
    getMockHost().emit('theme.changed', getMockHost().theme); // broadcast: the wrapper must leave non-overlay events alone
    await tick();
    expect(n).toBe(1);
  });
});

describe('mock menubar domain: dock.hide / dock.quit for the app menu', () => {
  it('hide moves the foreground to another running app', async () => {
    // dock.hide is allowed from dock, overlay and settings only (CALL_SURFACES): the app menu asks through the overlay
    const { overlay } = await surfaces();
    const host = getMockHost() as any;
    // the dock mock's fixture makes Microsoft Edge the foreground window at start
    expect(host.getActiveApp()).toBe(edge);
    await overlay.call('dock.hide', { itemId: edge });
    expect(host.getActiveApp()).not.toBe(edge);
    expect(host.getActiveApp()).not.toBeNull();
  });

  it('quit of the foreground app hands the foreground to another running app', async () => {
    const { bar } = await surfaces();
    const host = getMockHost() as any;
    host.setActiveApp(edge);
    await new MockBridge({ surface: 'overlay' }).call('dock.quit', { itemId: edge });
    expect(host.getActiveApp()).not.toBe(edge);
  });

  it('validates the payload like the real host: missing id is badRequest, unknown id notFound', async () => {
    const { overlay } = await surfaces();
    await expect(overlay.call('dock.quit', { itemId: '' })).rejects.toMatchObject({ code: 'badRequest' });
    await expect(overlay.call('dock.hide', { itemId: 'exe:c:\\nope.exe' })).rejects.toMatchObject({ code: 'notFound' });
  });
});
