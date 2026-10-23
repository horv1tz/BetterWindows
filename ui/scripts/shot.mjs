#!/usr/bin/env node
// Screenshot a surface (or the playground) rendered against the in-browser mock host.
//   node scripts/shot.mjs --route '#/dock' --out shots/dock.png [--width 1440] [--height 900] [--theme dark|light]
//        [--query 'lang=ru&w=1440'] [--wait 400] [--bg wallpaper|none] [--steps 'hover:.dock-item:2;wait:300;click:.x;eval:document.title']
// Starts its own Vite dev server on a random port and uses the preinstalled Chromium (no browser download).
import { createServer } from 'vite';
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => (a.startsWith('--') ? [...acc, [a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : 'true']] : acc), []));
const route = args.route ?? '#/dock';
const out = args.out ?? 'shots/out.png';
const width = Number(args.width ?? 1440), height = Number(args.height ?? 900);
const theme = args.theme ?? 'light';
const wait = Number(args.wait ?? 400);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const exe = process.env.PLAYWRIGHT_CHROMIUM
  ?? ['/opt/pw-browsers/chromium-1194/chrome-linux/chrome', '/opt/pw-browsers/chromium/chrome-linux/chrome'].find((p) => fs.existsSync(p));

const server = await createServer({ root, configFile: path.join(root, 'vite.config.ts'), server: { port: 0, host: '127.0.0.1' }, logLevel: 'error' });
await server.listen();
const base = server.resolvedUrls.local[0];
const browser = await chromium.launch({ executablePath: exe, args: ['--no-sandbox', '--disable-gpu'] });
try {
  const ctx = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: Number(args.scale ?? 1) });
  const page = await ctx.newPage();
  const logs = [];
  page.on('console', (m) => { if (['error', 'warning'].includes(m.type())) logs.push(`[${m.type()}] ${m.text()}`); });
  page.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`));
  const q = new URLSearchParams(args.query ?? '');
  q.set('theme', theme); q.set('mock', '1'); q.set('w', String(width)); q.set('h', String(height));
  await page.goto(`${base}?${q}${route}`);
  if ((args.bg ?? 'wallpaper') === 'wallpaper') {
    await page.addStyleTag({ content: 'html{background:linear-gradient(135deg,#2b59c3 0%,#7b4bd0 45%,#e8765b 100%) !important}' });
  }
  await page.waitForTimeout(wait);
  for (const step of (args.steps ?? '').split(';').filter(Boolean)) {
    const [cmd, ...rest] = step.split(':'); const val = rest.join(':');
    if (cmd === 'wait') await page.waitForTimeout(Number(val));
    else if (cmd === 'click') await page.click(val);
    else if (cmd === 'hover') { const [sel, nth] = val.split(':'); await page.locator(sel).nth(Number(nth ?? 0)).hover(); }
    else if (cmd === 'move') { const [x, y] = val.split(','); await page.mouse.move(Number(x), Number(y), { steps: 8 }); }
    else if (cmd === 'key') await page.keyboard.press(val);
    else if (cmd === 'type') await page.keyboard.type(val, { delay: 20 });
    else if (cmd === 'eval') console.log('eval ->', JSON.stringify(await page.evaluate(val)));
    else throw new Error(`unknown step '${cmd}'`);
  }
  await page.waitForTimeout(150);
  fs.mkdirSync(path.dirname(path.resolve(root, out)), { recursive: true });
  await page.screenshot({ path: path.resolve(root, out) });
  console.log(`saved ${out}`);
  if (logs.length) console.log(logs.join('\n'));
} finally {
  await browser.close();
  await server.close();
}
