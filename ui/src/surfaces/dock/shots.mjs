#!/usr/bin/env node
// Dev tool (ui-dock): renders the DOCK and the OVERLAY together against ONE shared mock host, like the real two-window app, so that
// dock menus / window lists can be screenshotted next to the dock they belong to. Same flags as scripts/shot.mjs plus:
//   --dockh 300        height of the dock iframe (the dock is anchored to its bottom edge)
//   --pos left|right   dock position (patches settings through the mock host before the dock mounts)
//   steps (page coordinates; selectors run in the dock iframe unless prefixed with "o"):
//     move:x,y  dhover:sel[:nth]  dclick:sel[:nth]  drclick:sel[:nth]  dmid:sel[:nth]  ohover/oclick/omid:sel[:nth]  key:Escape  wait:ms  eval:js (top page)
//     drag:fromSel:nth:toX,toY[:release=1]   (mouse drag from a dock element to page coordinates)
//   e.g. node src/surfaces/dock/shots.mjs --out shots/menu.png --theme dark --steps 'drclick:button.icon:1;wait:500'
import { createServer } from 'vite';
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const args = Object.fromEntries(process.argv.slice(2).reduce((acc, a, i, arr) => (a.startsWith('--') ? [...acc, [a.slice(2), arr[i + 1] && !arr[i + 1].startsWith('--') ? arr[i + 1] : 'true']] : acc), []));
const out = args.out ?? 'shots/dock-composite.png';
const width = Number(args.width ?? 1440), height = Number(args.height ?? 760), dockh = Number(args.dockh ?? 300);
const theme = args.theme ?? 'dark';
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const exe = process.env.PLAYWRIGHT_CHROMIUM ?? ['/opt/pw-browsers/chromium-1194/chrome-linux/chrome', '/opt/pw-browsers/chromium/chrome-linux/chrome'].find((p) => fs.existsSync(p));

const server = await createServer({ root, configFile: path.join(root, 'vite.config.ts'), server: { port: 0, host: '127.0.0.1' }, logLevel: 'error' });
await server.listen();
const base = server.resolvedUrls.local[0];
const browser = await chromium.launch({ executablePath: exe, args: ['--no-sandbox', '--disable-gpu'] });
try {
  const ctx = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: Number(args.scale ?? 1) });
  const page = await ctx.newPage();
  const logs = [];
  const watch = (p) => { p.on('console', (m) => { if (['error', 'warning'].includes(m.type())) logs.push(`[${m.type()}] ${m.text()}`); }); p.on('pageerror', (e) => logs.push(`[pageerror] ${e.message}`)); };
  watch(page);
  const q = new URLSearchParams(args.query ?? '');
  q.set('theme', theme); q.set('mock', '1'); q.set('w', String(width)); q.set('h', String(height));
  await page.goto(`${base}?${q}#/overlay`);
  await page.addStyleTag({ content: `html{background:${args.bg ?? 'linear-gradient(135deg,#2b59c3 0%,#7b4bd0 45%,#e8765b 100%)'} !important}` });
  // the overlay page is on top of the dock like the real overlay window, but empty areas must let the pointer through to the dock iframe
  await page.addStyleTag({ content: 'html,body,#app{pointer-events:none !important}#app *{pointer-events:auto}' });
  await page.waitForTimeout(200);
  if (args.pos) await page.evaluate((pos) => window.__bwMockHost && window.__bwMockHost.dispatchCall({ surface: 'settings', bridge: { deliver() {} } }, 'settings.patch', { patch: { dock: { position: pos } } }), args.pos);
  // the dock lives in an iframe at the bottom (bottom dock) or fills the height (side docks)
  const side = args.pos === 'left' || args.pos === 'right';
  const frameBox = side ? { x: 0, y: 0, w: dockh, h: height } : { x: 0, y: height - dockh, w: width, h: dockh };
  await page.evaluate(({ src, box, side, pos, width }) => {
    const f = document.createElement('iframe');
    f.src = src;
    f.setAttribute('allowtransparency', 'true');
    f.style.cssText = `position:fixed;border:0;background:transparent;left:${pos === 'right' ? width - box.w : box.x}px;top:${box.y}px;width:${box.w}px;height:${box.h}px;z-index:-1;pointer-events:auto`;
    document.body.appendChild(f);
    window.__bwMockHost.surfaceOffsets.dock = { x: pos === 'right' ? width - box.w : box.x, y: box.y };
  }, { src: `${base}?${q}#/dock`, box: frameBox, side, pos: args.pos, width });
  await page.waitForTimeout(Number(args.wait ?? 500));
  const dock = page.frames().find((f) => f !== page.mainFrame());
  watch(dock.page());
  const pick = (frame, v) => { const [sel, nth] = v.split(/:(?=\d+$)/); return frame.locator(sel).nth(Number(nth ?? 0)); };
  const center = async (loc) => { const b = await loc.boundingBox(); return { x: b.x + b.width / 2, y: b.y + b.height / 2 }; };
  for (const step of (args.steps ?? '').split(';').filter(Boolean)) {
    const [cmd, ...rest] = step.split(':'); const val = rest.join(':');
    if (cmd === 'wait') await page.waitForTimeout(Number(val));
    else if (cmd === 'move') { const [x, y] = val.split(','); await page.mouse.move(Number(x), Number(y), { steps: 10 }); }
    else if (cmd === 'key') await page.keyboard.press(val);
    else if (cmd === 'eval') console.log('eval ->', JSON.stringify(await page.evaluate(val)));
    else if (cmd === 'deval') console.log('deval ->', JSON.stringify(await dock.evaluate(val)));
    else if (/^[do](hover|click|rclick|mid)$/.test(cmd)) {
      const frame = cmd[0] === 'd' ? dock : page.mainFrame();
      const loc = pick(frame, val);
      const c = await center(loc);
      await page.mouse.move(c.x, c.y, { steps: 10 });
      const kind = cmd.slice(1);
      if (kind === 'click') await page.mouse.click(c.x, c.y);
      else if (kind === 'rclick') await page.mouse.click(c.x, c.y, { button: 'right' });
      else if (kind === 'mid') await page.mouse.click(c.x, c.y, { button: 'middle' });
    } else if (cmd === 'drag') {
      const m = val.match(/^(.*):(\d+):(-?\d+),(-?\d+)(?::(\d))?$/);
      const c = await center(dock.locator(m[1]).nth(Number(m[2])));
      await page.mouse.move(c.x, c.y, { steps: 6 });
      await page.mouse.down();
      await page.mouse.move(Number(m[3]), Number(m[4]), { steps: 18 });
      if (m[5] === '1') await page.mouse.up();
    } else if (cmd === 'mouseup') await page.mouse.up();
    else throw new Error(`unknown step '${cmd}'`);
  }
  await page.waitForTimeout(Number(args.settle ?? 250));
  fs.mkdirSync(path.dirname(path.resolve(root, out)), { recursive: true });
  await page.screenshot({ path: path.resolve(root, out) });
  console.log(`saved ${out}`);
  if (logs.length) console.log([...new Set(logs)].join('\n'));
} finally {
  await browser.close();
  await server.close();
}
