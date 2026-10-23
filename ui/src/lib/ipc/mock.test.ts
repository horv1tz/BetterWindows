import { afterEach, describe, expect, it } from 'vitest';
import { MockBridge } from '../../mock/mockBridge';
import { resetMockHost } from '../../mock/host';
import { PROTOCOL_VERSION } from '../protocol/generated';

afterEach(() => resetMockHost());

describe('MockBridge', () => {
  it('answers surface.ready with a complete SurfaceInit', async () => {
    const b = new MockBridge({ surface: 'dock' });
    const init = await b.call('surface.ready', { surface: 'dock' });
    expect(init.protocolVersion).toBe(PROTOCOL_VERSION);
    expect(init.settings.dock.iconSize).toBe(56);
    expect(init.monitor.primary).toBe(true);
  });

  it('settings.patch deep-merges and broadcasts to every surface', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    const bar = new MockBridge({ surface: 'menubar', monitorId: '1' });
    const seen: number[] = [];
    bar.on('settings.changed', (s) => seen.push(s.dock.iconSize));
    const res = await dock.call('settings.patch', { patch: { dock: { iconSize: 72 } } });
    await Promise.resolve();
    expect(res.dock.iconSize).toBe(72);
    expect(res.dock.magnification).toBe(true);
    expect(seen).toEqual([72]);
  });

  it('rejects unknown calls with notFound', async () => {
    const b = new MockBridge({ surface: 'dock' });
    await expect(b.call('dock.pin', { appId: 'x' })).rejects.toMatchObject({ code: 'notFound' });
  });
});
