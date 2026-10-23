import type { CallType, Calls, HostEventType, HostEvents, UiEventType, UiEvents } from '../protocol/generated';

export class BridgeError extends Error {
  constructor(public code: string, message: string) {
    super(message);
    this.name = 'BridgeError';
  }
}

type ParamsArg<K extends CallType> = Calls[K]['params'] extends void ? [] : [params: Calls[K]['params']];
type PayloadArg<K extends UiEventType> = UiEvents[K] extends void ? [] : [payload: UiEvents[K]];

/** Typed message bus between a surface (this page) and the host. Real = WebView2, mock = in-browser fake host. */
export interface Bridge {
  readonly kind: 'host' | 'mock';
  /** Request/response. Rejects with BridgeError(code, message) on { ok:false }. */
  call<K extends CallType>(type: K, ...args: ParamsArg<K>): Promise<Calls[K]['result']>;
  /** Subscribe to a host event. Returns an unsubscribe function. */
  on<K extends HostEventType>(type: K, handler: (payload: HostEvents[K]) => void): () => void;
  /** Fire-and-forget UI -> host event. */
  emit<K extends UiEventType>(type: K, ...args: PayloadArg<K>): void;
}

export interface Envelope {
  v: number;
  kind: 'event' | 'call' | 'result';
  id?: string;
  type?: string;
  payload?: unknown;
  ok?: boolean;
  error?: { code: string; message: string };
}
