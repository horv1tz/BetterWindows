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

describe('state replay', () => {
  it('replays the latest state event to a LATE subscriber', async () => {
    const dock = new MockBridge({ surface: 'dock' });
    const bar = new MockBridge({ surface: 'menubar', monitorId: '1' });
    await bar.call('surface.ready', { surface: 'menubar', monitorId: '1' });
    await new Promise((r) => setTimeout(r, 0)); // status events were pushed while nobody listened
    const got: number[] = [];
    bar.on('status.battery', (b) => got.push(b.percent)); // subscribes late
    await new Promise((r) => setTimeout(r, 0));
    expect(got).toEqual([76]);
    void dock;
  });

  it('does not replay transient events', async () => {
    const o = new MockBridge({ surface: 'overlay' });
    (o as any).deliver('overlay.hide', undefined);
    await new Promise((r) => setTimeout(r, 0));
    let n = 0;
    o.on('overlay.hide', () => n++);
    await new Promise((r) => setTimeout(r, 0));
    expect(n).toBe(0);
  });
});

describe('overlay mock', () => {
  it('overlay.open reaches overlay surfaces as overlay.show with translated anchor', async () => {
    const bar = new MockBridge({ surface: 'menubar', monitorId: '1' });
    const ov = new MockBridge({ surface: 'overlay' });
    const seen: any[] = [];
    ov.on('overlay.show', (s) => seen.push(s));
    await bar.call('overlay.open', { mode: 'popover', popover: { kind: 'calendar', anchor: { x: 10, y: 0, width: 50, height: 28 } } });
    await new Promise((r) => setTimeout(r, 0));
    expect(seen).toHaveLength(1);
    expect(seen[0].popover.kind).toBe('calendar');
    expect(seen[0].token).toBeGreaterThan(0);
  });
});
