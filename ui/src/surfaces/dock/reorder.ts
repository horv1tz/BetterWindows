/**
 * Drag & drop planning for the dock. Pure (unit tested): given where the pointer is, decide what a drop would do and how every
 * other slot must shift to open / close the gap. All numbers are strip coordinates along the main axis.
 */
export type DragKind = 'reorder' | 'pin';
export type DragVerdict = 'reorder' | 'pin' | 'unpin' | 'cancel';

export interface DragGeometry {
  kind: DragKind;
  /** Entry index of the dragged slot. */
  dragged: number;
  /** Entry indices of the pinned apps (kind 'pin') or of the dragged item's reorder group (kind 'reorder', includes `dragged`), in order. */
  group: number[];
  /** Rest centre of every entry (index aligned with the entries). */
  centers: number[];
  /** Distance between neighbouring icon centres: icon size + gap. */
  slot: number;
}

export interface DragPointer {
  /** Pointer position on the main axis, strip coordinates. */
  main: number;
  /** How far the pointer is beyond the pill's outer side on the cross axis (<= 0 while inside the pill). */
  outside: number;
}

export interface DragPlan {
  verdict: DragVerdict;
  /** reorder: new index inside the group; pin: index among the pinned apps (dock.pin `index`). */
  insertIndex: number;
  /** Per entry: main-axis shift (px) to animate to. For the dragged entry: where its slot would end up. */
  shifts: number[];
  /** Change of the strip length while the plan is previewed (a collapsing gap shortens the pill). */
  lengthDelta: number;
}

/** Number of `centers` strictly before `pos`: the insertion index of a pointer at `pos` among those slots. */
export function insertionIndex(centers: number[], pos: number): number {
  let n = 0;
  for (const c of centers) if (c < pos) n++;
  return n;
}

export function planDrag(g: DragGeometry, p: DragPointer, unpinDistance: number): DragPlan {
  const zero = () => new Array<number>(g.centers.length).fill(0);
  const cancel = (): DragPlan => ({ verdict: 'cancel', insertIndex: -1, shifts: zero(), lengthDelta: 0 });

  if (g.kind === 'reorder') {
    const gi = g.group.indexOf(g.dragged);
    if (gi < 0) return cancel();
    if (p.outside >= unpinDistance) {
      // dragged out of the dock: the gap collapses (everything after it moves up by one slot and the pill shortens)
      const shifts = zero();
      for (let e = g.dragged + 1; e < shifts.length; e++) shifts[e] = -g.slot;
      return { verdict: 'unpin', insertIndex: gi, shifts, lengthDelta: -g.slot };
    }
    const others = g.group.filter((e) => e !== g.dragged);
    const at = insertionIndex(others.map((e) => g.centers[e]), p.main);
    const shifts = zero();
    others.forEach((e, oi) => {
      const newPos = oi < at ? oi : oi + 1;
      shifts[e] = g.centers[g.group[newPos]] - g.centers[e];
    });
    shifts[g.dragged] = g.centers[g.group[at]] - g.centers[g.dragged];
    return { verdict: 'reorder', insertIndex: at, shifts, lengthDelta: 0 };
  }

  // kind === 'pin': a running, unpinned app is dragged towards the pinned apps
  if (p.outside >= unpinDistance) return cancel();
  const pins = g.group;
  const upper = pins.length ? g.centers[pins[pins.length - 1]] + g.slot * 0.8 : g.centers[0] + g.slot * 0.4;
  if (p.main > upper) return cancel();
  const at = insertionIndex(pins.map((e) => g.centers[e]), p.main);
  const insertEntry = at < pins.length ? pins[at] : pins.length ? pins[pins.length - 1] + 1 : 0;
  const shifts = zero();
  for (let e = insertEntry; e < g.dragged; e++) shifts[e] = g.slot;
  const landing = at < pins.length ? g.centers[pins[at]] : pins.length ? g.centers[pins[pins.length - 1]] + g.slot : g.centers[0];
  shifts[g.dragged] = landing - g.centers[g.dragged];
  return { verdict: 'pin', insertIndex: at, shifts, lengthDelta: 0 };
}

/** The full ordered pinned-id list for dock.reorder after moving `id` to `insertAt` inside its group (ids of that group in current order). */
export function reorderedPinned(allPinned: string[], groupIds: string[], id: string, insertAt: number): string[] {
  const others = groupIds.filter((x) => x !== id);
  const at = Math.max(0, Math.min(insertAt, others.length));
  const next = [...others.slice(0, at), id, ...others.slice(at)];
  const inGroup = new Set(groupIds);
  let k = 0;
  return allPinned.map((x) => (inGroup.has(x) ? next[k++] : x));
}

/** True when two id lists are identical (no-op reorder: do not call the host). */
export function sameOrder(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((x, i) => x === b[i]);
}

/** Movement (px) the pointer must travel before a press turns into a drag (below it the press is a click). */
export const DRAG_THRESHOLD = 6;

export function exceedsDragThreshold(dx: number, dy: number): boolean { return Math.hypot(dx, dy) >= DRAG_THRESHOLD; }

/** Distance beyond the pill after which a drop removes the item from the dock. */
export function unpinDistance(size: number): number { return Math.max(56, Math.round(size * 1.1)); }
