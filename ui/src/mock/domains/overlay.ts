import type { MockContext, MockHost } from '../host';
import type { OverlayRequest, OverlayShow, PopoverKind, Rect } from '../../lib/protocol/generated';

/**
 * FROZEN (scaffold): overlay.open / overlay.close and a dev shortcut to preview overlay content without the other surfaces:
 *   #/overlay?show=spotlight | launchpad | missionControl | popover:calendar | popover:controlCenter@1200,0,40,28
 * (query string BEFORE the hash:  http://localhost:5173/?show=popover:calendar#/overlay).
 * In the playground, surfaces register their window offsets in  host.surfaceOffsets[surface] = {x,y}  so anchors translate like in the real host.
 */
export default function register(host: MockHost) {
  let token = 0;
  (host as any).surfaceOffsets = {} as Record<string, { x: number; y: number }>;

  const show = (req: OverlayRequest, source?: MockContext) => {
    const off = (source && (host as any).surfaceOffsets[source.surface]) ?? { x: 0, y: 0 };
    const popover = req.popover ? { ...req.popover, anchor: { ...req.popover.anchor, x: req.popover.anchor.x + off.x, y: req.popover.anchor.y + off.y } } : undefined;
    const payload: OverlayShow = { mode: req.mode, popover, monitorId: req.monitorId ?? '1', token: ++token };
    host.emit('overlay.show', payload, (c) => c.surface === 'overlay');
    host.emit('surface.visibility', { visible: true, reason: 'overlay' }, (c) => c.surface === 'dock' && false);
  };

  host.handle('overlay.open', (p, ctx) => { show(p, ctx); });
  host.handle('overlay.close', () => { host.emit('overlay.hide', undefined as never, (c) => c.surface === 'overlay'); });
  (host as any).showOverlay = show;

  // ?show=... for a lone overlay page
  host.onSurfaceReady((ctx) => {
    if (ctx.surface !== 'overlay') return;
    const spec = new URLSearchParams(location.search).get('show');
    if (!spec) return;
    const [kindPart, anchorPart] = spec.split('@');
    const anchor: Rect = anchorPart ? (([x, y, width, height]) => ({ x, y, width, height }))(anchorPart.split(',').map(Number) as [number, number, number, number])
      : { x: Math.round(innerWidth - 120), y: 0, width: 40, height: 28 };
    setTimeout(() => {
      if (kindPart.startsWith('popover:')) show({ mode: 'popover', popover: { kind: kindPart.slice(8) as PopoverKind, anchor } });
      else show({ mode: kindPart as OverlayRequest['mode'] });
    }, 60);
  });
}
