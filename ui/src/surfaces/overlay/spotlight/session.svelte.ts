import type { Bridge } from '$lib/ipc';
import type { SearchPreview, SearchResult } from '$lib/protocol/generated';

/** What the list currently shows and which host request its ids belong to. */
export interface Shown {
  kind: 'recent' | 'results';
  requestId: string;
  results: SearchResult[];
}

export interface SessionOptions {
  /** Delay between the last keystroke and search.query. */
  debounceMs?: number;
  /** Delay between a selection change and search.preview. */
  previewDelayMs?: number;
}

/**
 * Reactive Spotlight search state machine, independent of the DOM:
 *  - a new requestId per (debounced) keystroke; only `search.results` of the LATEST request are rendered, each event replaces the list
 *  - the previous list stays visible while the next query loads (no flicker)
 *  - empty query -> search.recent; recents get their own requestId (needed for perform / preview)
 *  - search.preview is requested lazily for the selected result only (debounced, stale answers are ignored)
 */
export class SearchSession {
  query = $state('');
  shown = $state.raw<Shown>({ kind: 'recent', requestId: '', results: [] });
  /** All providers answered for the latest query (or nothing is being searched). */
  complete = $state(true);
  /** A query is debouncing or streaming. */
  searching = $state(false);
  /** The first answer for the latest query has arrived (false while the previous list is still shown). */
  answered = $state(false);
  error = $state('');
  preview = $state.raw<{ key: string; data: SearchPreview } | null>(null);
  previewLoading = $state(false);

  private readonly debounceMs: number;
  private readonly previewDelayMs: number;
  private active = '';
  private seq = 0;
  private readonly nonce = Math.random().toString(36).slice(2, 7);
  private timer: ReturnType<typeof setTimeout> | undefined;
  private previewTimer: ReturnType<typeof setTimeout> | undefined;
  private previewToken = 0;
  private recentToken = 0;
  private recentPending = false;
  private previewCache = new Map<string, SearchPreview>();
  private pendingRun: ((list: Shown) => void) | null = null;
  private offResults: () => void;
  private disposed = false;

  constructor(private readonly bridge: Bridge, opts: SessionOptions = {}) {
    this.debounceMs = opts.debounceMs ?? 30;
    this.previewDelayMs = opts.previewDelayMs ?? 120;
    this.offResults = bridge.on('search.results', (ev) => this.onResults(ev.requestId, ev.results, ev.complete));
    void this.loadRecent();
  }

  /** The shown list answers the newest request, so its ids can be performed/previewed. */
  get current(): boolean {
    if (this.recentPending) return false;
    if (!this.query.trim()) return this.shown.kind === 'recent';
    return this.answered && this.shown.requestId === this.active;
  }

  setQuery(q: string): void {
    if (this.disposed) return;
    this.query = q;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.pendingRun = null;
    this.error = '';
    const text = q.trim();
    if (!text) {
      this.cancelActive();
      this.searching = false;
      this.complete = true;
      this.answered = false;
      void this.loadRecent();
      return;
    }
    this.searching = true;
    this.complete = false;
    this.answered = false;
    this.timer = setTimeout(() => this.fire(text), this.debounceMs);
  }

  /** Send a debounced query right now (Enter while the debounce is pending). */
  flush(): void {
    if (this.timer === undefined) return;
    clearTimeout(this.timer);
    this.timer = undefined;
    this.fire(this.query.trim());
  }

  /** Run `fn` with the first answer of the latest query (user pressed Enter before it arrived). */
  runWhenReady(fn: (list: Shown) => void): void {
    this.pendingRun = fn;
    this.flush();
  }

  /** Execute an action. The host hides the overlay itself; the caller still closes optimistically. */
  perform(list: Shown, result: SearchResult, actionId?: string): Promise<void> {
    return this.bridge
      .call('search.perform', { requestId: list.requestId, resultId: result.id, actionId })
      .catch((e) => { this.error = e instanceof Error ? e.message : String(e); });
  }

  /** Ask for the preview of the selected result (or clear it with null). */
  select(result: SearchResult | null): void {
    clearTimeout(this.previewTimer);
    this.previewTimer = undefined;
    const token = ++this.previewToken;
    if (!result) { this.preview = null; this.previewLoading = false; return; }
    const inline = result.preview;
    if (inline && inline.kind !== 'none') {
      this.preview = { key: result.id, data: inline };
      this.previewLoading = false;
      return;
    }
    const requestId = this.shown.requestId;
    const key = `${requestId}:${result.id}`;
    const cached = this.previewCache.get(key);
    if (cached) { this.preview = { key: result.id, data: cached }; this.previewLoading = false; return; }
    // keep the old preview visible during the debounce only if it is for the same result; otherwise clear to avoid a wrong pane
    if (this.preview && this.preview.key !== result.id) this.preview = null;
    this.previewLoading = true;
    this.previewTimer = setTimeout(() => {
      this.bridge.call('search.preview', { requestId, resultId: result.id })
        .then((data) => {
          if (token !== this.previewToken || this.disposed) return; // a newer selection superseded this answer
          this.previewCache.set(key, data);
          this.preview = { key: result.id, data };
          this.previewLoading = false;
        })
        .catch(() => {
          if (token !== this.previewToken || this.disposed) return;
          this.preview = { key: result.id, data: { kind: 'none' } };
          this.previewLoading = false;
        });
    }, this.previewDelayMs);
  }

  dispose(): void {
    if (this.disposed) return;
    this.disposed = true;
    clearTimeout(this.timer);
    clearTimeout(this.previewTimer);
    this.offResults();
    this.cancelActive();
  }

  // ---- internals ----

  private nextId(): string { return `sp-${this.nonce}-${++this.seq}`; }

  private fire(text: string): void {
    this.timer = undefined;
    const id = this.nextId();
    this.active = id;
    this.previewCache.clear();
    this.bridge.call('search.query', { requestId: id, query: text }).catch((e) => {
      if (this.active !== id) return;
      this.searching = false;
      this.complete = true;
      this.error = e instanceof Error ? e.message : String(e);
    });
  }

  private cancelActive(): void {
    if (!this.active) return;
    const id = this.active;
    this.active = '';
    this.bridge.call('search.cancel', { requestId: id }).catch(() => { /* already finished */ });
  }

  private onResults(requestId: string, results: SearchResult[], complete: boolean): void {
    if (this.disposed || requestId !== this.active) return;
    this.shown = { kind: 'results', requestId, results };
    this.answered = true;
    this.complete = complete;
    this.searching = !complete;
    const run = this.pendingRun;
    if (run) { this.pendingRun = null; run(this.shown); }
  }

  private async loadRecent(): Promise<void> {
    const token = ++this.recentToken;
    this.recentPending = true;
    try {
      const r = await this.bridge.call('search.recent');
      if (token !== this.recentToken || this.disposed) return;
      // The user may have started typing while recents were loading: then the list belongs to the newer query.
      if (this.query.trim()) { this.recentPending = false; return; }
      this.shown = { kind: 'recent', requestId: r.requestId, results: r.results };
      const run = this.pendingRun;
      if (run) { this.pendingRun = null; run(this.shown); }
    } catch (e) {
      if (token === this.recentToken) this.error = e instanceof Error ? e.message : String(e);
    } finally {
      if (token === this.recentToken) this.recentPending = false;
    }
  }
}
