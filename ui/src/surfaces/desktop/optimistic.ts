// Optimistic UI state for drag/size/config edits: the surface shows the new value immediately and drops it once the host's
// desktop.state agrees (or the call failed, or a short grace period after success passed).
export type Overrides<T> = Record<string, Partial<T>>;

const same = (a: unknown, b: unknown) => a === b || JSON.stringify(a) === JSON.stringify(b);

/** Merge overrides over the host items (items that no longer exist are not resurrected). */
export function applyOverrides<T extends { id: string }>(items: readonly T[], overrides: Overrides<T>): T[] {
  return items.map((it) => (overrides[it.id] ? { ...it, ...overrides[it.id] } : it));
}

/** Drop override fields that the host state already reflects, and entries for items that are gone. */
export function pruneOverrides<T extends { id: string }>(overrides: Overrides<T>, items: readonly T[]): Overrides<T> {
  const byId = new Map(items.map((i) => [i.id, i]));
  const out: Overrides<T> = {};
  for (const [id, ov] of Object.entries(overrides)) {
    const host = byId.get(id);
    if (!host) continue;
    const rest: Partial<T> = {};
    for (const [k, v] of Object.entries(ov) as [keyof T, unknown][]) if (!same(host[k], v)) (rest as Record<keyof T, unknown>)[k] = v;
    if (Object.keys(rest).length) out[id] = rest;
  }
  return out;
}

/** Remove the given fields of one item's override. */
export function dropOverride<T>(overrides: Overrides<T>, id: string, keys?: ReadonlyArray<keyof T>): Overrides<T> {
  const cur = overrides[id];
  if (!cur) return overrides;
  const { [id]: _removed, ...rest } = overrides;
  if (!keys) return rest;
  const left: Partial<T> = { ...cur };
  for (const k of keys) delete left[k];
  return Object.keys(left).length ? { ...rest, [id]: left } : rest;
}
