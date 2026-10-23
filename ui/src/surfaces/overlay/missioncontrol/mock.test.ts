import { afterEach, describe, expect, it } from 'vitest';
import { MockBridge } from '../../../mock/mockBridge';
import { resetMockHost } from '../../../mock/host';
import type { Bridge } from '$lib/ipc';
import type { McSnapshot } from '$lib/protocol/generated';

afterEach(() => resetMockHost());

function overlay() {
  const bridge: Bridge = new MockBridge({ surface: 'overlay' });
  const changed: McSnapshot[] = [];
  bridge.on('mc.changed', (s) => changed.push(s));
  return { bridge, changed };
}

const flush = () => new Promise((r) => setTimeout(r, 0));

describe('mission control mock', () => {
  it('snapshots carry three desktops and windows spread over them, numbered by a growing rev', async () => {
    const { bridge } = overlay();
    const a = await bridge.call('mc.snapshot', {});
    const b = await bridge.call('mc.snapshot', {});
    expect(a.desktopsSupported).toBe(true);
    expect(a.desktops.map((d) => d.id)).toEqual(['d1', 'd2', 'd3']);
    expect(a.desktops.filter((d) => d.current)).toHaveLength(1);
    expect(new Set(a.windows.map((w) => w.desktopId)).size).toBe(3);
    expect(b.rev).toBeGreaterThan(a.rev);
    expect(a.windows.every((w) => w.thumbUrl !== undefined || w.minimized)).toBe(true);
  });

  it('mutations answer and push a mc.changed snapshot with a newer rev', async () => {
    const { bridge, changed } = overlay();
    const snap = await bridge.call('mc.snapshot', {});
    const victim = snap.windows.find((w) => w.desktopId === 'd1')!;
    await bridge.call('mc.closeWindow', { handle: victim.handle });
    await flush();
    expect(changed).toHaveLength(1);
    expect(changed[0]!.windows.some((w) => w.handle === victim.handle)).toBe(false);
    expect(changed[0]!.rev).toBeGreaterThan(snap.rev);
  });

  it('switchDesktop moves the current flag; unknown ids are notFound', async () => {
    const { bridge } = overlay();
    await bridge.call('mc.switchDesktop', { desktopId: 'd2' });
    const snap = await bridge.call('mc.snapshot', {});
    expect(snap.desktops.find((d) => d.current)?.id).toBe('d2');
    await expect(bridge.call('mc.switchDesktop', { desktopId: 'nope' })).rejects.toMatchObject({ code: 'notFound' });
  });

  it('createDesktop appends a desktop and removeDesktop refuses to remove the last one', async () => {
    const { bridge } = overlay();
    const created = await bridge.call('mc.createDesktop');
    expect(created.id).toBe('d4');
    const snap = await bridge.call('mc.snapshot', {});
    expect(snap.desktops).toHaveLength(4);
    for (let current = await bridge.call('mc.snapshot', {}); current.desktops.length > 1; current = await bridge.call('mc.snapshot', {})) {
      await bridge.call('mc.removeDesktop', { desktopId: current.desktops[0]!.id });
    }
    const left = await bridge.call('mc.snapshot', {});
    expect(left.desktops).toHaveLength(1);
    await expect(bridge.call('mc.removeDesktop', { desktopId: left.desktops[0]!.id })).rejects.toMatchObject({ code: 'badRequest' });
  });

  it('moveWindow places the window on the target desktop', async () => {
    const { bridge } = overlay();
    const snap = await bridge.call('mc.snapshot', {});
    const w = snap.windows.find((x) => x.desktopId === 'd1')!;
    await bridge.call('mc.moveWindow', { handle: w.handle, desktopId: 'd3' });
    const after = await bridge.call('mc.snapshot', {});
    expect(after.windows.find((x) => x.handle === w.handle)?.desktopId).toBe('d3');
  });

  it('only the overlay may call mission control', async () => {
    const dock: Bridge = new MockBridge({ surface: 'dock' });
    await expect(dock.call('mc.snapshot', {})).rejects.toMatchObject({ code: 'forbidden' });
  });
});
