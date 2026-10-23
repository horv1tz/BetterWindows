import { PROTOCOL_VERSION } from '../protocol/generated';
import type { CallType, HostEventType, UiEventType } from '../protocol/generated';
import { BridgeError, type Bridge, type Envelope } from './bridge';
import { Listeners } from './bridgeBase';

interface WebView2Api {
  postMessage(message: string): void;
  addEventListener(type: 'message', listener: (e: { data: unknown }) => void): void;
}

export function hasWebView2(): boolean {
  return typeof window !== 'undefined' && !!(window as unknown as { chrome?: { webview?: WebView2Api } }).chrome?.webview;
}

/**
 * Wire format: JSON strings in both directions.
 *   web -> host: chrome.webview.postMessage(JSON.stringify(envelope))     (host: TryGetWebMessageAsString)
 *   host -> web: CoreWebView2.PostWebMessageAsString(json)                 (here: event.data is a string)
 */
export class HostBridge implements Bridge {
  readonly kind = 'host' as const;
  private nextId = 1;
  private pending = new Map<string, { resolve: (v: any) => void; reject: (e: Error) => void; timer: ReturnType<typeof setTimeout> }>();
  private listeners = new Listeners();
  private wv: WebView2Api;

  constructor(private callTimeoutMs = 30_000) {
    this.wv = (window as unknown as { chrome: { webview: WebView2Api } }).chrome.webview;
    this.wv.addEventListener('message', (e) => this.receive(e.data));
  }

  private receive(data: unknown) {
    let env: Envelope;
    try {
      env = (typeof data === 'string' ? JSON.parse(data) : data) as Envelope;
    } catch {
      console.warn('[bridge] malformed message', data);
      return;
    }
    if (env.kind === 'result' && env.id) {
      const p = this.pending.get(env.id);
      if (!p) return;
      this.pending.delete(env.id);
      clearTimeout(p.timer);
      if (env.ok) p.resolve(env.payload);
      else p.reject(new BridgeError(env.error?.code ?? 'unknown', env.error?.message ?? 'call failed'));
    } else if (env.kind === 'event' && env.type) {
      this.listeners.deliver(env.type, env.payload);
    }
  }

  private send(env: Envelope) {
    this.wv.postMessage(JSON.stringify(env));
  }

  call<K extends CallType>(type: K, ...args: any[]): Promise<any> {
    const id = 'c' + this.nextId++;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new BridgeError('timeout', `call ${type} timed out`));
      }, this.callTimeoutMs);
      this.pending.set(id, { resolve, reject, timer });
      this.send({ v: PROTOCOL_VERSION, kind: 'call', id, type, payload: args[0] });
    });
  }

  on<K extends HostEventType>(type: K, handler: (payload: any) => void): () => void {
    return this.listeners.on(type, handler);
  }

  emit<K extends UiEventType>(type: K, ...args: any[]): void {
    this.send({ v: PROTOCOL_VERSION, kind: 'event', type, payload: args[0] });
  }
}
