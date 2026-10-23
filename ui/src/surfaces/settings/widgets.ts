import type { WidgetInstance, WidgetSize, WidgetType } from '$lib/protocol/generated';

export const WIDGET_TYPES: readonly WidgetType[] = ['clock', 'calendar', 'weather', 'reminders', 'media', 'battery', 'notes'];
export const WIDGET_SIZES: readonly WidgetSize[] = ['small', 'medium', 'large'];

/** The size pre-selected in the gallery for each widget type. */
export const DEFAULT_WIDGET_SIZE: Record<WidgetType, WidgetSize> = {
  clock: 'small', calendar: 'medium', weather: 'medium', reminders: 'medium', media: 'medium', battery: 'small', notes: 'medium',
};

export const widgetCounts = (widgets: readonly WidgetInstance[]): Record<WidgetType, number> => {
  const out = Object.fromEntries(WIDGET_TYPES.map((t) => [t, 0])) as Record<WidgetType, number>;
  for (const w of widgets) if (w.type in out) out[w.type]++;
  return out;
};

/** Widgets on one monitor; `null` monitorId in the settings means the primary monitor. */
export function widgetsOnMonitor(widgets: readonly WidgetInstance[], monitorId: string, primaryId: string): WidgetInstance[] {
  return widgets.filter((w) => (w.monitorId ?? primaryId) === monitorId);
}
