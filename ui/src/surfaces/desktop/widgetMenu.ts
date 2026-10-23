// Pure model of the widget context menu: which entries a widget has and what choosing one means.
import type { WidgetInstance, WidgetSize } from '$lib/protocol/generated';
import type { MenuItem } from './menu';

export type MenuAction =
  | { kind: 'size'; size: WidgetSize }
  | { kind: 'remove' }
  | { kind: 'config'; config: Record<string, unknown> }
  | { kind: 'settings'; page: string };

type T = (key: string) => string;

const objectConfig = (c: unknown): Record<string, unknown> => (c && typeof c === 'object' && !Array.isArray(c) ? (c as Record<string, unknown>) : {});

/** Menu entries for a widget: its own options first, then the three sizes, then "Remove Widget". */
export function widgetMenu(widget: WidgetInstance, t: T): { items: MenuItem[]; resolve: (id: string) => MenuAction | null } {
  const cfg = objectConfig(widget.config);
  const actions = new Map<string, MenuAction>();
  const items: MenuItem[] = [];
  const own: MenuItem[] = [];

  if (widget.type === 'clock') {
    const style = cfg.style === 'digital' ? 'digital' : 'analog';
    own.push({ id: 'style.analog', label: t('clock.analog'), checked: style === 'analog' }, { id: 'style.digital', label: t('clock.digital'), checked: style === 'digital' });
    actions.set('style.analog', { kind: 'config', config: { ...cfg, style: 'analog' } });
    actions.set('style.digital', { kind: 'config', config: { ...cfg, style: 'digital' } });
    own.push({ id: 'sep.clock', label: '', separator: true }, { id: 'seconds', label: t('clock.seconds'), checked: cfg.seconds === true });
    actions.set('seconds', { kind: 'config', config: { ...cfg, seconds: cfg.seconds !== true } });
  } else if (widget.type === 'calendar') {
    own.push({ id: 'weekNumbers', label: t('cal.weekNumbers'), checked: cfg.weekNumbers === true });
    actions.set('weekNumbers', { kind: 'config', config: { ...cfg, weekNumbers: cfg.weekNumbers !== true } });
  } else if (widget.type === 'weather') {
    own.push({ id: 'location', label: t('weather.setLocation') });
    actions.set('location', { kind: 'settings', page: 'widgets' });
  }
  if (own.length) items.push(...own, { id: 'sep.own', label: '', separator: true });

  for (const size of ['small', 'medium', 'large'] as const) {
    items.push({ id: `size.${size}`, label: t(`w.${size}`), checked: widget.size === size });
    actions.set(`size.${size}`, { kind: 'size', size });
  }
  items.push({ id: 'sep.remove', label: '', separator: true }, { id: 'remove', label: t('w.remove'), danger: true });
  actions.set('remove', { kind: 'remove' });

  return { items, resolve: (id) => actions.get(id) ?? null };
}
