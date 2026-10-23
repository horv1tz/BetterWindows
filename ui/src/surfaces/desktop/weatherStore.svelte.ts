// One weather feed for all weather widgets: weather.get on first use, every 10 minutes, and whenever the location changes.
import { getBridge } from '$lib/ipc';
import type { WeatherSnapshot } from '$lib/protocol/generated';

const REFRESH_MS = 10 * 60_000;

let snapshot = $state<WeatherSnapshot | null>(null);
let loading = $state(false);
let failed = $state(false);
let failMessage = $state('');
let users = 0;
let timer: ReturnType<typeof setInterval> | null = null;
let inflight: Promise<void> | null = null;
let locationKey = '';

async function fetchWeather(force: boolean): Promise<void> {
  if (inflight) return inflight;
  loading = true;
  inflight = (async () => {
    try {
      const s = await getBridge().call('weather.get', { force });
      snapshot = s;
      failed = false;
      failMessage = '';
    } catch (e) {
      failed = true;
      failMessage = e instanceof Error ? e.message : String(e);
    } finally {
      loading = false;
      inflight = null;
    }
  })();
  return inflight;
}

export const weatherStore = {
  get snapshot() { return snapshot; },
  get loading() { return loading; },
  get failed() { return failed; },
  get message() { return failMessage; },
  /** Force a refresh now (the Retry button, location change). */
  refresh(force = true) { return fetchWeather(force); },
  /** A different location than the last one we fetched for: fetch again, bypassing the host's cache. */
  locationChanged(key: string) {
    if (key === locationKey) return;
    const first = locationKey === '';
    locationKey = key;
    if (!first) void fetchWeather(true);
  },
  /** Register a widget; the first one starts the 10-minute timer. Returns the unregister function. */
  use(): () => void {
    users++;
    if (users === 1) {
      void fetchWeather(false);
      timer = setInterval(() => void fetchWeather(true), REFRESH_MS);
    } else if (!snapshot && !loading) void fetchWeather(false);
    return () => {
      users--;
      if (users <= 0) { users = 0; if (timer) clearInterval(timer); timer = null; }
    };
  },
  /** Test hook. */
  _reset() { snapshot = null; loading = false; failed = false; failMessage = ''; users = 0; inflight = null; locationKey = ''; if (timer) clearInterval(timer); timer = null; },
};

export const WEATHER_REFRESH_MS = REFRESH_MS;
