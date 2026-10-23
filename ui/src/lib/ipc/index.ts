import type { SurfaceInit, SurfaceKind } from '../protocol/generated';
import type { Bridge } from './bridge';
import { HostBridge, hasWebView2 } from './hostBridge';
import { MockBridge } from '../../mock/mockBridge';

export type { Bridge } from './bridge';
export { BridgeError } from './bridge';

export interface SurfaceRoute {
  surface: SurfaceKind;
  monitorId?: string;
}

/** `#/dock`, `#/menubar/2`, `#/overlay`, `#/desktop/1`, `#/settings`, `#/playground` */
export function parseRoute(hash: string = location.hash): SurfaceRoute | 'playground' {
  const parts = hash.replace(/^#\/?/, '').split('?')[0].split('/').filter(Boolean);
  if (parts[0] === 'playground') return 'playground';
  const surface = (['dock', 'menubar', 'overlay', 'desktop', 'settings'] as const).find((s) => s === parts[0]) ?? 'dock';
  return { surface, monitorId: parts[1] };
}

let _bridge: Bridge | null = null;
let _init: SurfaceInit | null = null;

/** The process-wide bridge for this page (HostBridge inside WebView2, MockBridge in a normal browser or with ?mock). */
export function getBridge(route?: SurfaceRoute): Bridge {
  if (_bridge) return _bridge;
  const forceMock = new URLSearchParams(location.search).has('mock');
  if (hasWebView2() && !forceMock) _bridge = new HostBridge();
  else _bridge = new MockBridge(route ?? (parseRoute() as SurfaceRoute));
  return _bridge;
}

export function setBridgeForTests(b: Bridge | null) { _bridge = b; }

/** Result of surface.ready; available after bootstrap() in main.ts. */
export function getInit(): SurfaceInit {
  if (!_init) throw new Error('surface not initialised yet');
  return _init;
}
export function setInit(i: SurfaceInit | null) { _init = i; }
