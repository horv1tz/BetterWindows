// Widget registry: type -> content component + the extra context-menu entries of that widget.
import type { Component } from 'svelte';
import type { WidgetInstance, WidgetSize, WidgetType } from '$lib/protocol/generated';
import ClockWidget from './ClockWidget.svelte';
import CalendarWidget from './CalendarWidget.svelte';
import WeatherWidget from './WeatherWidget.svelte';
import RemindersWidget from './RemindersWidget.svelte';
import MediaWidget from './MediaWidget.svelte';
import BatteryWidget from './BatteryWidget.svelte';
import NotesWidget from './NotesWidget.svelte';

/** What every widget content component receives. `saveConfig` REPLACES the widget's config object (desktop.updateWidget semantics). */
export interface WidgetProps {
  widget: WidgetInstance;
  config: Record<string, unknown>;
  size: WidgetSize;
  saveConfig: (config: Record<string, unknown>) => void;
}

export interface WidgetDef {
  component: Component<WidgetProps>;
}

export const WIDGETS: Record<WidgetType, WidgetDef> = {
  clock: { component: ClockWidget },
  calendar: { component: CalendarWidget },
  weather: { component: WeatherWidget },
  reminders: { component: RemindersWidget },
  media: { component: MediaWidget },
  battery: { component: BatteryWidget },
  notes: { component: NotesWidget },
};
