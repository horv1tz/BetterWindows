// Mock of the weather service (Open-Meteo in the real host): weather.get / weather.search / weather.setLocation with a deterministic,
// realistic fixture (current conditions, 24 hourly entries, 7 daily). Owner: ui-desktop.
// Query flags (before the hash):  ?weather=error | error-once | unavailable | none | slow
import { BridgeError } from '../../lib/ipc/bridge';
import type { WeatherDay, WeatherHour, WeatherLocation, WeatherSnapshot } from '../../lib/protocol/generated';
import type { MockHost } from '../host';
import { hash, query } from './widgets';

const pad = (n: number) => String(n).padStart(2, '0');
const isoHour = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:00`;
const isoDay = (d: Date) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const round1 = (v: number) => Math.round(v * 10) / 10;

interface Place extends WeatherLocation { alt?: string }

export const CITIES: readonly Place[] = [
  { name: 'Moscow', alt: 'Москва', country: 'Russia', admin: 'Moscow', latitude: 55.7558, longitude: 37.6173 },
  { name: 'Saint Petersburg', alt: 'Санкт-Петербург', country: 'Russia', admin: 'Saint Petersburg', latitude: 59.9343, longitude: 30.3351 },
  { name: 'Kazan', alt: 'Казань', country: 'Russia', admin: 'Tatarstan', latitude: 55.7887, longitude: 49.1221 },
  { name: 'Novosibirsk', alt: 'Новосибирск', country: 'Russia', admin: 'Novosibirsk Oblast', latitude: 55.0084, longitude: 82.9357 },
  { name: 'Kyiv', alt: 'Киев', country: 'Ukraine', admin: 'Kyiv', latitude: 50.4501, longitude: 30.5234 },
  { name: 'Minsk', alt: 'Минск', country: 'Belarus', admin: 'Minsk', latitude: 53.9006, longitude: 27.5590 },
  { name: 'London', alt: 'Лондон', country: 'United Kingdom', admin: 'England', latitude: 51.5072, longitude: -0.1276 },
  { name: 'London', country: 'Canada', admin: 'Ontario', latitude: 42.9849, longitude: -81.2453 },
  { name: 'Berlin', alt: 'Берлин', country: 'Germany', admin: 'Berlin', latitude: 52.52, longitude: 13.405 },
  { name: 'Paris', alt: 'Париж', country: 'France', admin: 'Île-de-France', latitude: 48.8566, longitude: 2.3522 },
  { name: 'Madrid', alt: 'Мадрид', country: 'Spain', admin: 'Community of Madrid', latitude: 40.4168, longitude: -3.7038 },
  { name: 'Rome', alt: 'Рим', country: 'Italy', admin: 'Lazio', latitude: 41.9028, longitude: 12.4964 },
  { name: 'Istanbul', alt: 'Стамбул', country: 'Türkiye', admin: 'Istanbul', latitude: 41.0082, longitude: 28.9784 },
  { name: 'New York', alt: 'Нью-Йорк', country: 'United States', admin: 'New York', latitude: 40.7128, longitude: -74.006 },
  { name: 'San Francisco', alt: 'Сан-Франциско', country: 'United States', admin: 'California', latitude: 37.7749, longitude: -122.4194 },
  { name: 'Los Angeles', alt: 'Лос-Анджелес', country: 'United States', admin: 'California', latitude: 34.0522, longitude: -118.2437 },
  { name: 'Tokyo', alt: 'Токио', country: 'Japan', admin: 'Tokyo', latitude: 35.6762, longitude: 139.6503 },
  { name: 'Dubai', alt: 'Дубай', country: 'United Arab Emirates', admin: 'Dubai', latitude: 25.2048, longitude: 55.2708 },
  { name: 'Sydney', alt: 'Сидней', country: 'Australia', admin: 'New South Wales', latitude: -33.8688, longitude: 151.2093 },
  { name: 'Reykjavik', alt: 'Рейкьявик', country: 'Iceland', admin: 'Capital Region', latitude: 64.1466, longitude: -21.9426 },
];

/** Case-insensitive search over the English and Russian names; prefix matches first, at most `limit` results. */
export function searchCities(queryText: string, limit = 8): WeatherLocation[] {
  const needle = queryText.trim().toLowerCase();
  if (!needle) return [];
  const score = (c: Place) => {
    const names = [c.name.toLowerCase(), (c.alt ?? '').toLowerCase()];
    if (names.some((n) => n === needle)) return 0;
    if (names.some((n) => n.startsWith(needle))) return 1;
    if (names.some((n) => n.includes(needle)) || (c.country ?? '').toLowerCase().includes(needle) || (c.admin ?? '').toLowerCase().includes(needle)) return 2;
    return 9;
  };
  return CITIES.map((c) => ({ c, s: score(c) })).filter((x) => x.s < 9).sort((a, b) => a.s - b.s).slice(0, limit)
    .map(({ c }) => ({ name: c.name, country: c.country, admin: c.admin, latitude: c.latitude, longitude: c.longitude }));
}

/** Weather code for a slot: persists for 3-hour blocks; snow instead of rain when it is freezing. */
function pickCode(seed: string, block: number, tempC: number): number {
  const r = hash(`${seed}:${block}`) % 100;
  if (r < 22) return 0;
  if (r < 40) return 1;
  if (r < 60) return 2;
  if (r < 76) return 3;
  if (r < 86) return tempC < 0.5 ? 71 : 61;
  if (r < 92) return tempC < 0.5 ? 85 : 80;
  if (r < 96) return 45;
  return tempC > 8 ? 95 : 73;
}

/** Deterministic fixture for a location and moment (pure, unit-tested). */
export function makeWeather(loc: { name: string; latitude: number; longitude: number }, now: Date): WeatherSnapshot {
  const lat = loc.latitude;
  const doy = (Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()) - Date.UTC(now.getFullYear(), 0, 0)) / 86400000;
  const seasonal = Math.cos(((doy - 200) / 365) * 2 * Math.PI) * (lat >= 0 ? 1 : -1);
  const mean = 31 - 0.42 * Math.abs(lat);
  const amp = 0.28 * Math.abs(lat) + 2;
  const base = mean + amp * seasonal;
  const summer = (seasonal + 1) / 2;
  const sunrise = 8 - 3 * summer, sunset = 17 + 4 * summer;
  const isDayAt = (h: number) => h >= Math.round(sunrise) && h < Math.round(sunset);
  const seed = `${loc.name}:${isoDay(now)}`;
  const diurnal = (h: number) => 4.2 * Math.sin(((h - 9) / 24) * 2 * Math.PI);

  const hourly: WeatherHour[] = [];
  const t0 = new Date(now.getFullYear(), now.getMonth(), now.getDate(), now.getHours());
  for (let i = 0; i < 24; i++) {
    const d = new Date(t0.getTime() + i * 3600_000);
    const tempC = round1(base + diurnal(d.getHours()) + ((hash(`${seed}:t${i}`) % 11) - 5) * 0.08);
    hourly.push({ time: isoHour(d), tempC, code: pickCode(seed, Math.floor((d.getTime() / 3600_000) / 3), tempC) });
  }

  const daily: WeatherDay[] = [];
  for (let i = 0; i < 7; i++) {
    const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() + i);
    const wobble = ((hash(`${loc.name}:${isoDay(d)}`) % 100) / 100 - 0.5) * 6;
    const highC = round1(base + 3.2 + wobble);
    const lowC = round1(highC - 6.5 - (hash(`${isoDay(d)}:low`) % 30) / 10);
    daily.push({ date: isoDay(d), highC, lowC, code: pickCode(`${loc.name}:${isoDay(d)}`, 4, highC) });
  }
  // today's range must contain the hourly values
  const todayTemps = hourly.filter((h) => h.time.startsWith(isoDay(now))).map((h) => h.tempC);
  if (todayTemps.length) {
    daily[0].highC = round1(Math.max(daily[0].highC, ...todayTemps));
    daily[0].lowC = round1(Math.min(daily[0].lowC, ...todayTemps));
  }

  const cur = hourly[0];
  return {
    available: true,
    location: loc.name,
    tempC: cur.tempC,
    apparentC: round1(cur.tempC - 1.5 - (hash(seed) % 20) / 10),
    code: cur.code,
    isDay: isDayAt(now.getHours()),
    humidity: 45 + (hash(`${seed}:h`) % 45),
    windKph: round1(4 + (hash(`${seed}:w`) % 220) / 10),
    hourly, daily,
    updatedAt: `${isoDay(now)}T${pad(now.getHours())}:${pad(now.getMinutes())}:00`,
  };
}

const EMPTY: WeatherSnapshot = { available: false, location: '', tempC: 0, apparentC: 0, code: 0, isDay: true, hourly: [], daily: [] };

export default function register(host: MockHost) {
  const mode = query().get('weather') ?? '';
  let calls = 0;
  let cache: { key: string; at: number; snap: WeatherSnapshot } | null = null;
  const delay = (ms: number) => new Promise((r) => setTimeout(r, ms));

  host.handle('weather.get', async (p) => {
    const w = host.settings.weather;
    const hasLoc = typeof w.latitude === 'number' && typeof w.longitude === 'number' && !!w.locationName;
    const key = `${w.locationName}:${w.latitude}:${w.longitude}`;
    if (!p.force && cache && cache.key === key && Date.now() - cache.at < 10 * 60_000) return structuredClone(cache.snap);
    calls++;
    await delay(mode === 'slow' ? 2500 : 400);
    if (mode === 'error' || (mode === 'error-once' && calls === 1)) throw new BridgeError('failed', 'The weather service is unreachable');
    if (mode === 'unavailable') return { ...EMPTY, error: 'Request timed out', location: w.locationName };
    if (mode === 'none' || !hasLoc) return { ...EMPTY };
    const snap = makeWeather({ name: w.locationName, latitude: w.latitude!, longitude: w.longitude! }, new Date());
    cache = { key, at: Date.now(), snap };
    return structuredClone(snap);
  });

  host.handle('weather.search', async (p) => {
    if (typeof p.query !== 'string') throw new BridgeError('badRequest', 'query is required');
    if (p.query.length > 100) throw new BridgeError('badRequest', 'query is too long');
    await delay(150);
    return { locations: searchCities(p.query) };
  });

  host.handle('weather.setLocation', (p) => {
    if (typeof p.name !== 'string' || !p.name.trim()) throw new BridgeError('badRequest', 'name is required');
    if (!Number.isFinite(p.latitude) || Math.abs(p.latitude) > 90 || !Number.isFinite(p.longitude) || Math.abs(p.longitude) > 180) throw new BridgeError('badRequest', 'coordinates out of range');
    host.settings = { ...host.settings, weather: { ...host.settings.weather, locationName: p.name.trim().slice(0, 120), latitude: p.latitude, longitude: p.longitude } };
    cache = null;
    host.emit('settings.changed', host.settings);
  });
}
