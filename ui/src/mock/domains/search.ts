import { APPS, art, appByName } from '../fixtures';
import { BridgeError } from '../../lib/ipc/bridge';
import { currentLocale } from '../../lib/i18n.svelte';
import { fold } from '../../surfaces/overlay/spotlight/highlight';
import type { SearchAction, SearchPreview, SearchProviderId, SearchResult } from '../../lib/protocol/generated';
import type { MockHost } from '../host';

/**
 * Mock of the Core search engine (owner: ui-overlay-search). Behaves like the real one:
 *  - search.query returns immediately; fast providers (calc, apps, settings, commands, windows, web) stream first,
 *    `files` arrives after ~350 ms with complete=true. EVERY event carries the complete merged, ordered list.
 *  - a new query (or search.cancel) cancels the previous request; ids of older requests stop resolving (notFound).
 *  - search.recent returns its own requestId; search.preview is lazy (text head / image url / none).
 *  - search.perform logs, copies calculator answers and hides the overlay (the real host does the same).
 */

// ---------------------------------------------------------------------------------------------------------------------
// Calculator: a tiny recursive-descent evaluator. No eval/Function: only numbers, + - * / ^ %, parentheses, a few functions.
// ---------------------------------------------------------------------------------------------------------------------

export interface CalcOutcome { value: number; /** true when the input is a bare number (not worth a calculator card). */ trivial: boolean }

const FUNCS: Record<string, (x: number) => number> = {
  sqrt: Math.sqrt, abs: Math.abs, sin: Math.sin, cos: Math.cos, tan: Math.tan, ln: Math.log, log: Math.log10, exp: Math.exp,
  round: Math.round, floor: Math.floor, ceil: Math.ceil, cbrt: Math.cbrt,
};
const CONSTS: Record<string, number> = { pi: Math.PI, e: Math.E };

/** Evaluate an arithmetic expression; null when it is not a valid expression (or not finite). */
export function evaluateExpression(input: string): CalcOutcome | null {
  const src = input
    .replace(/[×·]/g, '*').replace(/÷/g, '/').replace(/−/g, '-').replace(/\*\*/g, '^')
    .replace(/(\d),(\d)/g, '$1.$2') // 12,5 -> 12.5
    .replace(/(\d)\s*[xх]\s*(?=[\d(])/gi, '$1*'); // 3 x 4 (latin x or cyrillic х)
  if (!src.trim() || src.length > 200) return null;
  let i = 0;
  let trivial = true;
  const peek = () => { while (src[i] === ' ') i++; return src[i]; };
  const fail = (): never => { throw new Error('parse'); };

  function number(): number {
    const m = /^(\d+\.?\d*|\.\d+)(e[+-]?\d+)?/i.exec(src.slice(i));
    if (!m) return fail();
    // "2e" followed by a letter is the constant e, not an exponent: only accept the exponent form when digits follow
    i += m[0].length;
    return parseFloat(m[0]);
  }
  function primary(): number {
    const c = peek();
    if (c === '(') {
      i++;
      trivial = false;
      const v = expr();
      if (peek() !== ')') return fail();
      i++;
      return v;
    }
    if (c !== undefined && /[a-z]/i.test(c)) {
      const m = /^[a-z]+/i.exec(src.slice(i))![0].toLowerCase();
      i += m.length;
      if (m in CONSTS) return CONSTS[m]; // a lone constant is not worth a calculator card
      trivial = false;
      const f = FUNCS[m];
      if (!f) return fail();
      if (peek() !== '(') return fail();
      i++;
      const v = expr();
      if (peek() !== ')') return fail();
      i++;
      return f(v);
    }
    return number();
  }
  function postfix(): number {
    let v = primary();
    while (peek() === '%') { i++; trivial = false; v /= 100; }
    return v;
  }
  function power(): number {
    const base = postfix();
    if (peek() === '^') { i++; trivial = false; return Math.pow(base, unary()); }
    return base;
  }
  function unary(): number {
    const c = peek();
    if (c === '-') { i++; return -unary(); }
    if (c === '+') { i++; return unary(); }
    return power();
  }
  function term(): number {
    let v = unary();
    for (;;) {
      const c = peek();
      if (c === '*') { i++; trivial = false; v *= unary(); }
      else if (c === '/') { i++; trivial = false; v /= unary(); }
      else return v;
    }
  }
  function expr(): number {
    let v = term();
    for (;;) {
      const c = peek();
      if (c === '+') { i++; trivial = false; v += term(); }
      else if (c === '-') { i++; trivial = false; v -= term(); }
      else return v;
    }
  }
  try {
    const v = expr();
    if (peek() !== undefined) return null;
    if (!Number.isFinite(v)) return null;
    return { value: v, trivial };
  } catch {
    return null;
  }
}

/** 84, 0.30000000000000004 -> "0.3", 1234567.891 -> "1 234 567.891" (display only). */
export function formatAnswer(v: number, locale = 'en'): string {
  const rounded = Number(v.toPrecision(12));
  return new Intl.NumberFormat(locale === 'ru' ? 'ru-RU' : 'en-US', { maximumFractionDigits: 10, useGrouping: true }).format(rounded);
}

// ---------------------------------------------------------------------------------------------------------------------
// Data
// ---------------------------------------------------------------------------------------------------------------------

type Bi = { en: string; ru: string };
const tr = (b: Bi) => (currentLocale() === 'ru' ? b.ru : b.en);

interface SettingsPage { uri: string; name: Bi; area: Bi; keywords: string }
const SETTINGS_PAGES: SettingsPage[] = [
  { uri: 'ms-settings:display', name: { en: 'Display', ru: 'Дисплей' }, area: { en: 'System', ru: 'Система' }, keywords: 'screen resolution brightness scale монитор экран разрешение яркость масштаб' },
  { uri: 'ms-settings:sound', name: { en: 'Sound', ru: 'Звук' }, area: { en: 'System', ru: 'Система' }, keywords: 'volume speakers microphone audio громкость динамики микрофон' },
  { uri: 'ms-settings:notifications', name: { en: 'Notifications', ru: 'Уведомления' }, area: { en: 'System', ru: 'Система' }, keywords: 'focus do not disturb alerts не беспокоить фокусировка' },
  { uri: 'ms-settings:nightlight', name: { en: 'Night light', ru: 'Ночной свет' }, area: { en: 'System · Display', ru: 'Система · Дисплей' }, keywords: 'blue light warm color ночной режим тёплый' },
  { uri: 'ms-settings:powersleep', name: { en: 'Power & battery', ru: 'Питание и батарея' }, area: { en: 'System', ru: 'Система' }, keywords: 'sleep battery saver energy сон батарея экономия' },
  { uri: 'ms-settings:storagesense', name: { en: 'Storage', ru: 'Память' }, area: { en: 'System', ru: 'Система' }, keywords: 'disk space cleanup диск место очистка хранилище' },
  { uri: 'ms-settings:bluetooth', name: { en: 'Bluetooth & devices', ru: 'Bluetooth и устройства' }, area: { en: 'Devices', ru: 'Устройства' }, keywords: 'pair headphones mouse keyboard наушники мышь клавиатура' },
  { uri: 'ms-settings:mousetouchpad', name: { en: 'Mouse', ru: 'Мышь' }, area: { en: 'Bluetooth & devices', ru: 'Bluetooth и устройства' }, keywords: 'pointer cursor scroll speed указатель курсор прокрутка' },
  { uri: 'ms-settings:network-wifi', name: { en: 'Wi-Fi', ru: 'Wi-Fi' }, area: { en: 'Network & internet', ru: 'Сеть и Интернет' }, keywords: 'wireless network internet беспроводная сеть интернет' },
  { uri: 'ms-settings:network-vpn', name: { en: 'VPN', ru: 'VPN' }, area: { en: 'Network & internet', ru: 'Сеть и Интернет' }, keywords: 'virtual private network впн' },
  { uri: 'ms-settings:personalization-background', name: { en: 'Background', ru: 'Фон' }, area: { en: 'Personalization', ru: 'Персонализация' }, keywords: 'wallpaper desktop picture обои рабочий стол картинка' },
  { uri: 'ms-settings:colors', name: { en: 'Colors', ru: 'Цвета' }, area: { en: 'Personalization', ru: 'Персонализация' }, keywords: 'accent dark mode light theme тёмная тема акцент' },
  { uri: 'ms-settings:taskbar', name: { en: 'Taskbar', ru: 'Панель задач' }, area: { en: 'Personalization', ru: 'Персонализация' }, keywords: 'tray icons start панель задач значки' },
  { uri: 'ms-settings:appsfeatures', name: { en: 'Installed apps', ru: 'Установленные приложения' }, area: { en: 'Apps', ru: 'Приложения' }, keywords: 'uninstall programs удалить программы' },
  { uri: 'ms-settings:defaultapps', name: { en: 'Default apps', ru: 'Приложения по умолчанию' }, area: { en: 'Apps', ru: 'Приложения' }, keywords: 'browser file types браузер по умолчанию' },
  { uri: 'ms-settings:startupapps', name: { en: 'Startup apps', ru: 'Автозагрузка' }, area: { en: 'Apps', ru: 'Приложения' }, keywords: 'autostart login items автозапуск' },
  { uri: 'ms-settings:yourinfo', name: { en: 'Your info', ru: 'Ваши данные' }, area: { en: 'Accounts', ru: 'Учётные записи' }, keywords: 'account profile picture аккаунт профиль' },
  { uri: 'ms-settings:dateandtime', name: { en: 'Date & time', ru: 'Дата и время' }, area: { en: 'Time & language', ru: 'Время и язык' }, keywords: 'clock timezone часы часовой пояс' },
  { uri: 'ms-settings:regionlanguage', name: { en: 'Language & region', ru: 'Язык и регион' }, area: { en: 'Time & language', ru: 'Время и язык' }, keywords: 'keyboard layout input раскладка клавиатуры ввод' },
  { uri: 'ms-settings:easeofaccess-display', name: { en: 'Accessibility', ru: 'Специальные возможности' }, area: { en: 'Accessibility', ru: 'Специальные возможности' }, keywords: 'text size contrast narrator размер текста контраст экранный диктор' },
  { uri: 'ms-settings:privacy', name: { en: 'Privacy & security', ru: 'Конфиденциальность и защита' }, area: { en: 'Privacy & security', ru: 'Конфиденциальность и защита' }, keywords: 'camera location permissions камера местоположение разрешения' },
  { uri: 'ms-settings:windowsupdate', name: { en: 'Windows Update', ru: 'Центр обновления Windows' }, area: { en: 'Windows Update', ru: 'Центр обновления Windows' }, keywords: 'updates patches обновления' },
];

interface Command { id: string; name: Bi; sub: Bi; keywords: string; confirm: boolean; icon?: undefined }
const COMMANDS: Command[] = [
  { id: 'sleep', name: { en: 'Sleep', ru: 'Спящий режим' }, sub: { en: 'Put this PC to sleep', ru: 'Перевести компьютер в спящий режим' }, keywords: 'suspend standby сон', confirm: false },
  { id: 'lock', name: { en: 'Lock Screen', ru: 'Заблокировать экран' }, sub: { en: 'Lock this PC', ru: 'Заблокировать компьютер' }, keywords: 'lock блокировка', confirm: false },
  { id: 'logout', name: { en: 'Log Out', ru: 'Выйти из системы' }, sub: { en: 'Sign out of Windows · Asks for confirmation', ru: 'Завершить сеанс Windows · Потребуется подтверждение' }, keywords: 'sign out logoff выход завершить сеанс', confirm: true },
  { id: 'restart', name: { en: 'Restart', ru: 'Перезагрузить' }, sub: { en: 'Restart this PC · Asks for confirmation', ru: 'Перезагрузить компьютер · Потребуется подтверждение' }, keywords: 'reboot перезагрузка', confirm: true },
  { id: 'shutdown', name: { en: 'Shut Down', ru: 'Выключить компьютер' }, sub: { en: 'Turn off this PC · Asks for confirmation', ru: 'Выключить компьютер · Потребуется подтверждение' }, keywords: 'power off завершение работы выключение', confirm: true },
  { id: 'explorer', name: { en: 'Restart Explorer', ru: 'Перезапустить Проводник' }, sub: { en: 'Restart the Windows shell', ru: 'Перезапустить оболочку Windows' }, keywords: 'explorer.exe shell проводник оболочка', confirm: false },
  { id: 'bin', name: { en: 'Empty Recycle Bin', ru: 'Очистить корзину' }, sub: { en: 'Delete all items for good · Asks for confirmation', ru: 'Удалить всё навсегда · Потребуется подтверждение' }, keywords: 'trash delete мусор удалить', confirm: true },
  { id: 'darkmode', name: { en: 'Toggle Dark Mode', ru: 'Переключить тёмную тему' }, sub: { en: 'Switch between light and dark appearance', ru: 'Переключить светлое и тёмное оформление' }, keywords: 'theme appearance night тема оформление', confirm: false },
  { id: 'quit', name: { en: 'Quit BetterWindows', ru: 'Выйти из BetterWindows' }, sub: { en: 'Restore the Windows shell and exit', ru: 'Вернуть оболочку Windows и закрыть программу' }, keywords: 'exit close выход закрыть', confirm: false },
];

interface MockFile { name: string; dir: string; kind: 'text' | 'image' | 'doc'; body?: string }
const HOME = 'C:\\Users\\you';
const FILES: MockFile[] = [
  { name: 'Quarterly report.docx', dir: `${HOME}\\Documents`, kind: 'doc' },
  { name: 'Budget 2026.xlsx', dir: `${HOME}\\Documents\\Finance`, kind: 'doc' },
  { name: 'Meeting notes.txt', dir: `${HOME}\\Documents`, kind: 'text', body: 'Weekly sync, Tuesday\n\n- Ship Spotlight preview pane\n- Launchpad folders: drag app onto app\n- Decide on tray strategy (Explorer stays the shell)\n- Review hotkey conflicts with PowerToys\n\nAction items: Anna → icons, Max → search index.' },
  { name: 'editor-config.json', dir: `${HOME}\\Projects\\betterwindows`, kind: 'text', body: '{\n  "tabSize": 2,\n  "insertSpaces": true,\n  "formatOnSave": true,\n  "theme": "dark",\n  "fontFamily": "Inter, Segoe UI"\n}' },
  { name: 'README.md', dir: `${HOME}\\Projects\\betterwindows`, kind: 'text', body: '# BetterWindows\n\nA macOS-style shell for Windows 11.\n\n## Build\n\n    dotnet build BetterWindows.sln\n    cd ui && npm run build\n\n## Hotkeys\n\n- Alt+Space  Spotlight\n- Ctrl+Alt+L  Launchpad' },
  { name: 'Vacation 2025.jpg', dir: `${HOME}\\Pictures`, kind: 'image' },
  { name: 'Screenshot edit.png', dir: `${HOME}\\Pictures\\Screenshots`, kind: 'image' },
  { name: 'Resume.pdf', dir: `${HOME}\\Documents`, kind: 'doc' },
  { name: 'Презентация проекта.pptx', dir: `${HOME}\\Documents`, kind: 'doc' },
  { name: 'Резюме.docx', dir: `${HOME}\\Documents`, kind: 'doc' },
  { name: 'Заметки по встрече.txt', dir: `${HOME}\\Documents`, kind: 'text', body: 'Встреча в среду\n\n- Показать предпросмотр в Spotlight\n- Папки в Launchpad\n- Обсудить горячие клавиши\n\nЗадачи: Аня — иконки, Макс — индекс поиска.' },
  { name: 'Edit presets.xmp', dir: `${HOME}\\Pictures\\Presets`, kind: 'text', body: '<x:xmpmeta>\n  <rdf:Description crs:Exposure2012="+0.35" crs:Contrast2012="+12"/>\n</x:xmpmeta>' },
  { name: 'Downloads', dir: HOME, kind: 'doc' },
];

const WINDOWS: Array<{ app: string; title: string; handle: string }> = [
  { app: 'Mail', title: 'Inbox - Mail', handle: '6001' },
  { app: 'Visual Studio Code', title: 'search.ts - betterwindows - Visual Studio Code', handle: '6002' },
  { app: 'Microsoft Edge', title: 'Edge: BetterWindows docs', handle: '6003' },
  { app: 'Terminal', title: 'PowerShell', handle: '6004' },
];

const PROVIDER_ORDER: SearchProviderId[] = ['calc', 'apps', 'settings', 'commands', 'windows', 'files', 'web'];

// ---------------------------------------------------------------------------------------------------------------------
// Scoring (0..1)
// ---------------------------------------------------------------------------------------------------------------------

/** Score `text` against folded query tokens: prefix > word start > contains > initials; 0 = no match. */
export function scoreText(text: string, query: string, extra = ''): number {
  const q = fold(query).trim();
  if (!q) return 0;
  const hay = fold(text);
  const words = hay.split(/[^\p{L}\p{N}]+/u).filter(Boolean);
  const tokens = q.split(/\s+/);
  let total = 0;
  for (const t of tokens) {
    let s = 0;
    if (hay.startsWith(t)) s = 1;
    else if (words.some((w) => w.startsWith(t))) s = 0.85;
    else if (hay.includes(t)) s = 0.65;
    else if (t.length >= 2 && words.map((w) => w[0]).join('').includes(t)) s = 0.55;
    else if (extra && fold(extra).includes(t)) s = 0.4;
    if (!s) return 0;
    total += s;
  }
  return total / tokens.length;
}

// ---------------------------------------------------------------------------------------------------------------------
// Result construction
// ---------------------------------------------------------------------------------------------------------------------

const act = (id: string, label: Bi, primary = false): SearchAction => ({ id, label: tr(label), primary });
const OPEN: Bi = { en: 'Open', ru: 'Открыть' };
const SHOW_IN_FOLDER: Bi = { en: 'Show in folder', ru: 'Показать в папке' };
const COPY_PATH: Bi = { en: 'Copy path', ru: 'Копировать путь' };
const COPY: Bi = { en: 'Copy', ru: 'Копировать' };
const RUN: Bi = { en: 'Run', ru: 'Выполнить' };
const SWITCH_TO: Bi = { en: 'Switch to', ru: 'Перейти' };
const REVEAL: Bi = { en: 'Show in Explorer', ru: 'Показать в Проводнике' };

function appResults(query: string, limit: number): SearchResult[] {
  return APPS.map((a) => ({ a, s: scoreText(a.name, query, a.category ?? '') }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s || x.a.name.localeCompare(y.a.name))
    .slice(0, limit)
    .map(({ a, s }) => ({
      id: `app:${a.id}`, provider: 'apps' as const, title: a.name, subtitle: a.category ?? undefined, iconUrl: a.iconUrl, score: 0.6 + s * 0.4,
      actions: [act('open', OPEN, true), ...(a.path ? [act('reveal', REVEAL)] : [])],
    }));
}

function settingsResults(query: string, limit: number): SearchResult[] {
  return SETTINGS_PAGES.map((p) => ({ p, s: Math.max(scoreText(tr(p.name), query, p.keywords), scoreText(p.name.en, query, p.keywords)) }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s)
    .slice(0, limit)
    .map(({ p, s }) => ({
      id: `settings:${p.uri}`, provider: 'settings' as const, title: tr(p.name), subtitle: tr(p.area), score: 0.45 + s * 0.4,
      actions: [act('open', OPEN, true)],
    }));
}

function commandResults(query: string, limit: number): SearchResult[] {
  return COMMANDS.map((c) => ({ c, s: Math.max(scoreText(tr(c.name), query, c.keywords), scoreText(c.name.en, query, c.keywords)) }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s)
    .slice(0, limit)
    .map(({ c, s }) => ({
      id: `cmd:${c.id}`, provider: 'commands' as const, title: tr(c.name), subtitle: tr(c.sub), score: 0.4 + s * 0.4,
      actions: [act('run', RUN, true)],
    }));
}

function windowResults(query: string, limit: number): SearchResult[] {
  return WINDOWS.map((w) => ({ w, s: scoreText(w.title, query) }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s)
    .slice(0, limit)
    .map(({ w, s }) => ({
      id: `win:${w.handle}`, provider: 'windows' as const, title: w.title, subtitle: w.app, iconUrl: appByName(w.app).iconUrl, score: 0.5 + s * 0.4,
      actions: [act('activate', SWITCH_TO, true)],
    }));
}

function fileResults(query: string, limit: number): SearchResult[] {
  return FILES.map((f) => ({ f, s: scoreText(f.name, query) }))
    .filter((x) => x.s > 0)
    .sort((x, y) => y.s - x.s || x.f.name.localeCompare(y.f.name))
    .slice(0, limit)
    .map(({ f, s }) => ({
      id: `file:${f.dir}\\${f.name}`, provider: 'files' as const, title: f.name, subtitle: f.dir, score: 0.3 + s * 0.4,
      actions: [act('open', OPEN, true), act('reveal', SHOW_IN_FOLDER), act('copyPath', COPY_PATH)],
    }));
}

function webResult(query: string): SearchResult[] {
  const q = query.trim();
  if (q.length < 2) return [];
  const label = currentLocale() === 'ru' ? `Искать в интернете: «${q}»` : `Search the web for “${q}”`;
  return [{ id: 'web:search', provider: 'web', title: label, subtitle: 'duckduckgo.com', score: 0.05, actions: [act('open', OPEN, true)] }];
}

function calcResult(query: string): SearchResult[] {
  const out = evaluateExpression(query);
  if (!out || out.trivial) return [];
  const answer = formatAnswer(out.value, currentLocale());
  return [{
    id: 'calc:result', provider: 'calc', title: answer, subtitle: query.trim(), score: 1,
    actions: [act('copy', COPY, true)],
    preview: { kind: 'text', text: `${query.trim()} = ${answer}` },
  }];
}

/** Merge provider outputs in the canonical order (calc, apps, settings, commands, windows, files, web). */
export function mergeResults(parts: Partial<Record<SearchProviderId, SearchResult[]>>): SearchResult[] {
  return PROVIDER_ORDER.flatMap((p) => parts[p] ?? []);
}

// ---------------------------------------------------------------------------------------------------------------------
// Host registration
// ---------------------------------------------------------------------------------------------------------------------

export default function register(host: MockHost) {
  const maxPerProvider = () => Math.max(3, Math.min(host.settings.spotlight.maxResults, 6));
  /** requestId -> results resolvable by perform/preview (only the newest request + recents stay alive, like the real engine). */
  let alive = new Map<string, Map<string, SearchResult>>();
  let active = '';
  let timers: Array<ReturnType<typeof setTimeout>> = [];
  let recentSeq = 0;

  const toOverlay = (c: { surface: string }) => c.surface === 'overlay';
  const stopActive = () => { timers.forEach(clearTimeout); timers = []; };
  const register_ = (requestId: string, results: SearchResult[]) => {
    const m = alive.get(requestId) ?? new Map<string, SearchResult>();
    for (const r of results) m.set(r.id, r);
    alive.set(requestId, m);
  };

  host.handle('search.query', (p) => {
    if (!p.requestId || p.requestId.length > 128) throw new BridgeError('badRequest', 'requestId is required');
    if (typeof p.query !== 'string' || p.query.length > 512) throw new BridgeError('badRequest', 'query too long');
    stopActive();
    alive = new Map(); // a new query invalidates every older request (recents included)
    active = p.requestId;
    const id = p.requestId;
    const q = p.query.trim();
    const cap = maxPerProvider();
    const fast = {
      calc: calcResult(q),
      apps: appResults(q, cap),
      settings: settingsResults(q, Math.min(cap, 4)),
      commands: commandResults(q, 3),
      windows: windowResults(q, 3),
      web: webResult(q),
    };
    const files = fileResults(q, cap);
    const emit = (results: SearchResult[], complete: boolean) => {
      if (active !== id) return;
      register_(id, results);
      host.emit('search.results', { requestId: id, results, complete }, toOverlay);
    };
    timers.push(setTimeout(() => emit(mergeResults(fast), false), 12));
    timers.push(setTimeout(() => emit(mergeResults({ ...fast, files }), true), 350));
  });

  host.handle('search.cancel', (p) => {
    if (p.requestId === active) { stopActive(); active = ''; alive.delete(p.requestId); }
  });

  host.handle('search.recent', () => {
    const requestId = `recent-${++recentSeq}`;
    const results: SearchResult[] = [
      ...['Visual Studio Code', 'Microsoft Edge', 'Notepad'].map((n) => {
        const a = appByName(n);
        return { id: `app:${a.id}`, provider: 'apps' as const, title: a.name, subtitle: a.category ?? undefined, iconUrl: a.iconUrl, score: 1, actions: [act('open', OPEN, true)] };
      }),
      ...fileResults('report', 1), ...fileResults('notes', 1),
      ...settingsResults('display', 1),
    ];
    // recents live until the next search.query, like every other request
    alive = new Map(alive);
    register_(requestId, results);
    return { requestId, results };
  });

  host.handle('search.preview', async (p): Promise<SearchPreview> => {
    const r = alive.get(p.requestId)?.get(p.resultId);
    if (!r) throw new BridgeError('notFound', 'unknown result');
    await new Promise((res) => setTimeout(res, 70)); // reading the file head
    if (r.provider !== 'files') return { kind: 'none' };
    const file = FILES.find((f) => r.id === `file:${f.dir}\\${f.name}`);
    if (!file) return { kind: 'none' };
    if (file.kind === 'text' && file.body) return { kind: 'text', text: file.body };
    if (file.kind === 'image') return { kind: 'image', imageUrl: art(file.name, 480, 320) };
    return { kind: 'none' };
  });

  host.handle('search.perform', (p) => {
    const r = alive.get(p.requestId)?.get(p.resultId);
    if (!r) throw new BridgeError('notFound', 'result is no longer resolvable');
    const action = r.actions.find((a) => a.id === p.actionId) ?? r.actions.find((a) => a.primary);
    if (p.actionId && !r.actions.some((a) => a.id === p.actionId)) throw new BridgeError('badRequest', `unknown action '${p.actionId}'`);
    console.info('[mock] search.perform', r.provider, r.title, action?.id);
    if (r.provider === 'calc' && action?.id === 'copy') { try { void navigator.clipboard?.writeText(r.title.replace(/\s/g, '')); } catch { /* clipboard unavailable */ } }
    // the real host hides the overlay itself when an action runs
    setTimeout(() => host.emit('overlay.hide', undefined as never, toOverlay), 0);
  });
}
