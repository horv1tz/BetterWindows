import type { DockItem } from '$lib/protocol/generated';
import { isTouch, mainOf, outsideOf, slotCenterClient, type RectLike } from './dom';
import type { DockMode, DockPos, Metrics, StripLayout } from './layout';
import { appIdOf, dragKind, pinnedAppIndices, pinnedIds, reorderGroup, type Entry } from './model';
import type { DockMotion, Frame } from './motion';
import { exceedsDragThreshold, planDrag, reorderedPinned, sameOrder, unpinDistance, type DragKind, type DragPlan } from './reorder';

/**
 * Drag & drop of dock icons: pinned items reorder (live gap) or unpin (dragged far out, with a "poof"), running unpinned apps can be
 * dragged into the pinned area to pin them. The decisions live in reorder.ts (pure); this class owns the pointer state machine,
 * the ghost, the settle animation and the optimistic reorder. The Surface hands it what it needs through `DragHost`.
 */
export interface DragHost {
  readonly pos: DockPos;
  readonly m: Metrics;
  readonly entries: Entry[];
  readonly strip: StripLayout;
  /** Rest pill rectangle in client coordinates (refreshed through refreshRect before each use). */
  readonly stageRect: RectLike;
  readonly frame: Frame | null;
  readonly items: DockItem[];
  readonly motion: DockMotion;
  /** True while the pointer hovers the dock (magnification active). */
  readonly pointerInside: boolean;
  refreshRect(): void;
  wake(): void;
  render(): void;
  wantMode(mode: DockMode): void;
  /** The drag takes over: stop hovering (magnification, tooltip). */
  stopHover(): void;
  /** The protocol calls a drop can make; they resolve to false when the host refused (the UI then falls back). */
  reorder(itemIds: string[]): Promise<boolean>;
  pin(appId: string, index: number): Promise<boolean>;
  unpin(itemId: string): Promise<boolean>;
  /** Local pinned order shown until the host confirms (null = none). */
  setOptimisticOrder(order: string[] | null): void;
  openMenu(key: string): void;
}

interface Press {
  key: string; index: number; kind: DragKind | null;
  x: number; y: number;
  /** Where inside the (magnified) icon the pointer grabbed it, as an offset from the icon centre in rest-size px. */
  grabX: number; grabY: number;
  timer?: ReturnType<typeof setTimeout>;
}

export type Settle = null | 'land' | 'return' | 'poof';
export interface Drag { key: string; index: number; kind: DragKind; plan: DragPlan; x: number; y: number; settle: Settle }

const LONG_PRESS_MS = 550;

export class DragController {
  /** The running drag (reactive: the Surface renders the ghost from it). */
  drag = $state<Drag | null>(null);
  /** Key of the icon whose slot is an empty gap (dragged, or waiting for the host's confirmation after the drop). */
  hiddenKey = $state<string | null>(null);
  /** 'pin' drags need room for the inserted slot, so the window is asked to be wider. */
  dragMode = $state<DragKind | null>(null);

  private press: Press | null = null;
  private holdTimer: ReturnType<typeof setTimeout> | undefined;
  private settleTimer: ReturnType<typeof setTimeout> | undefined;
  private suppress = false;
  private lastKeys = '';
  private optimisticDone = false;
  private optimisticTimer: ReturnType<typeof setTimeout> | undefined;

  constructor(private host: DragHost) {}

  get dragging(): boolean { return this.drag !== null; }
  /** A press is in progress or a drag is running: pointer moves belong to us. */
  get busy(): boolean { return this.press !== null || this.drag !== null; }

  /** True once after a drag / long press: the click that follows must not activate anything. */
  consumeClick(): boolean {
    const s = this.suppress;
    this.suppress = false;
    return s;
  }

  dispose(): void {
    for (const t of [this.holdTimer, this.settleTimer, this.optimisticTimer, this.press?.timer]) clearTimeout(t);
  }

  // ---------------------------------------------------------------- pointer

  down(e: PointerEvent): void {
    if (e.button !== 0) return;
    this.suppress = false;
    const el = (e.target as Element).closest<HTMLElement>('[data-key]');
    const key = el?.dataset.key;
    const h = this.host;
    if (!el || !key || this.drag) return;
    const index = h.entries.findIndex((x) => x.key === key);
    if (index < 0) return;
    h.refreshRect();
    const entry = h.entries[index];
    const fs = h.frame?.slots[index];
    const c = slotCenterClient(h.pos, h.stageRect, h.m, h.strip.slots[index], fs?.offset ?? 0);
    const sc = fs?.scale ?? 1;
    const press: Press = { key, index, kind: dragKind(entry), x: e.clientX, y: e.clientY, grabX: (e.clientX - c.x) / sc, grabY: (e.clientY - c.y) / sc };
    this.press = press;
    try { el.setPointerCapture(e.pointerId); } catch { /* synthetic events cannot be captured: they still bubble */ }
    if (isTouch(e) && entry.kind === 'item') {
      // touch has no right button: a long press opens the dock menu instead (and the click that follows is swallowed)
      press.timer = setTimeout(() => {
        if (this.press !== press || this.drag) return;
        this.suppress = true;
        this.press = null;
        h.openMenu(key);
      }, LONG_PRESS_MS);
    }
  }

  move(e: PointerEvent): void {
    const press = this.press;
    if (!press) return;
    // the button was released somewhere we did not see (capture lost, released over another window): no phantom press / drag
    if (!(e.buttons & 1)) { this.cancel(); return; }
    if (!this.drag) {
      if (!exceedsDragThreshold(e.clientX - press.x, e.clientY - press.y)) return;
      if (!this.start(press, e)) { this.press = null; return; }
    }
    const d = this.drag;
    if (!d || d.settle) return;
    const plan = this.planFor(d, press, e);
    this.drag = { ...d, x: e.clientX - press.grabX, y: e.clientY - press.grabY, plan };
    this.host.motion.setDrag(plan.shifts, plan.lengthDelta);
    this.host.wake();
  }

  up(e: PointerEvent): void {
    const press = this.press;
    if (press && !this.drag) { clearTimeout(press.timer); this.press = null; return; }
    const d = this.drag;
    if (!press || !d || d.settle) return;
    this.host.refreshRect();
    this.finish(d, this.planFor(d, press, e));
  }

  /** pointercancel: a press is forgotten, a drag flies home. */
  cancel(): void {
    if (this.press && !this.drag) { clearTimeout(this.press.timer); this.press = null; }
    else if (this.drag && !this.drag.settle) this.abort();
  }

  escape(): boolean {
    if (!this.drag || this.drag.settle) return false;
    this.abort();
    return true;
  }

  private start(press: Press, e: PointerEvent): boolean {
    clearTimeout(press.timer);
    if (!press.kind) return false; // minimised tiles, separators, launching placeholders: not draggable (the press stays a click)
    const h = this.host;
    this.dragMode = press.kind;
    this.drag = { key: press.key, index: press.index, kind: press.kind, plan: { verdict: 'cancel', insertIndex: -1, shifts: [], lengthDelta: 0 }, x: e.clientX, y: e.clientY, settle: null };
    this.hiddenKey = press.key;
    if (h.pointerInside) h.stopHover();
    h.wantMode('drag');
    this.suppress = true;
    h.wake();
    return true;
  }

  private planFor(d: Drag, press: Press, e: PointerEvent): DragPlan {
    const h = this.host;
    const group = d.kind === 'reorder' ? reorderGroup(h.entries, d.index) : pinnedAppIndices(h.entries);
    return planDrag(
      { kind: d.kind, dragged: d.index, group, centers: h.strip.slots.map((s) => s.start + s.len / 2), slot: h.m.size + h.m.gap },
      { main: mainOf(h.pos, h.stageRect, h.m, e.clientX - press.grabX, e.clientY - press.grabY), outside: outsideOf(h.pos, h.stageRect, h.m, e.clientX, e.clientY) },
      unpinDistance(h.m.size),
    );
  }

  // ---------------------------------------------------------------- drop

  private finish(d: Drag, plan: DragPlan): void {
    const h = this.host;
    const entry = h.entries[d.index];
    if (entry?.kind !== 'item') { this.abort(); return; }
    const item = entry.item;
    const landing = () => slotCenterClient(h.pos, h.stageRect, h.m, h.strip.slots[d.index], plan.shifts[d.index]);

    if (plan.verdict === 'reorder') {
      const all = pinnedIds(h.items);
      const group = reorderGroup(h.entries, d.index).map((i) => h.entries[i].key);
      const order = reorderedPinned(all, group, item.id, plan.insertIndex);
      const to = landing();
      this.drag = { ...d, plan, x: to.x, y: to.y };
      this.settleIn(210, 'land');
      if (!sameOrder(all, order)) {
        // show the new order right away; the host's dock.state confirms it (or the call fails and we fall back)
        this.optimisticDone = false;
        h.setOptimisticOrder(order);
        this.hold();
        void h.reorder(order).then((ok) => {
          if (!ok) h.setOptimisticOrder(null);
          this.optimisticDone = true;
          this.clearOptimisticIfSettled();
        });
      }
      return;
    }
    if (plan.verdict === 'pin') {
      const to = landing();
      this.drag = { ...d, plan, x: to.x, y: to.y };
      this.settleIn(210, 'land');
      this.hold();
      void h.pin(appIdOf(item), plan.insertIndex);
      return;
    }
    if (plan.verdict === 'unpin') {
      this.drag = { ...d, plan };
      this.settleIn(420, 'poof');
      this.hold();
      void h.unpin(item.id);
      return;
    }
    this.abort(d);
  }

  /** Fly the ghost back to its slot and close the gap. */
  private abort(d: Drag | null = this.drag): void {
    if (!d) return;
    const h = this.host;
    const home = slotCenterClient(h.pos, h.stageRect, h.m, h.strip.slots[d.index], 0);
    h.motion.setDrag(h.strip.slots.map(() => 0), 0);
    this.drag = { ...d, x: home.x, y: home.y };
    this.settleIn(190, 'return');
  }

  private settleIn(ms: number, how: Settle): void {
    if (!this.drag) return;
    this.drag = { ...this.drag, settle: how };
    clearTimeout(this.settleTimer);
    this.settleTimer = setTimeout(() => this.end(), ms);
  }

  private end(): void {
    const h = this.host;
    clearTimeout(this.settleTimer);
    this.drag = null;
    this.press = null;
    this.dragMode = null;
    // a pending hold (waiting for dock.state) keeps the preview shifts and the hidden source icon until it is released
    if (this.holdTimer === undefined) { h.motion.setDrag(null); this.hiddenKey = null; }
    h.wantMode(h.pointerInside ? 'hover' : 'idle');
    h.wake();
  }

  /** After the drop the layout becomes authoritative once dock.state arrives; until then keep the preview shifts so nothing jumps back. */
  private hold(): void {
    clearTimeout(this.holdTimer);
    this.holdTimer = setTimeout(() => {
      this.holdTimer = undefined;
      this.host.motion.setDrag(null);
      this.hiddenKey = null;
      this.host.wake();
    }, 1000);
  }

  private releaseHold(): void {
    if (this.holdTimer === undefined) return;
    clearTimeout(this.holdTimer);
    this.holdTimer = undefined;
    this.host.motion.resetShifts();
    if (!this.drag) this.hiddenKey = null; // otherwise the landing ghost still covers the slot
    this.host.render();
  }

  /** Call whenever the entries may have changed (an effect in the Surface): the keys decide whether the preview is still valid. */
  layoutChanged(): void {
    const keys = this.host.entries.map((e) => e.key).join('|');
    if (keys === this.lastKeys) return;
    this.lastKeys = keys;
    if (this.drag && !this.drag.settle) { this.abort(); return; } // a window closed / state pushed under a running drag: the preview is stale
    this.releaseHold();
  }

  // ---------------------------------------------------------------- optimistic order

  /** dock.state arrived: drop the local order once the host agrees with it (or the reorder call is done). */
  stateChanged(optimistic: string[] | null): void {
    if (!optimistic) return;
    if (sameOrder(pinnedIds(this.host.items), optimistic) || this.optimisticDone) { this.host.setOptimisticOrder(null); this.optimisticDone = false; return; }
    clearTimeout(this.optimisticTimer);
    this.optimisticTimer = setTimeout(() => { this.host.setOptimisticOrder(null); this.optimisticDone = false; }, 1500);
  }

  private clearOptimisticIfSettled(): void {
    if (this.optimisticDone) { this.host.setOptimisticOrder(null); this.optimisticDone = false; clearTimeout(this.optimisticTimer); }
  }
}
