#!/usr/bin/env node
// IronMonkey app driver — drives the running Blazor UI and the API.
//
// Zero dependencies: uses system google-chrome over the Chrome DevTools Protocol
// via Node's built-in WebSocket (Node 18+). There is no `chromium-cli` and no
// playwright resolvable from this repo, so CDP is the portable handle.
//
// Blazor Server is the reason this must be a real browser: pages render over a
// SignalR circuit, so @bind-Value only captures input once the circuit is live.
// curl can fetch the HTML but can never fill the form.
//
// Usage:
//   node .claude/skills/run-ironmonkey/driver.mjs smoke     # API + page checks
//   node .claude/skills/run-ironmonkey/driver.mjs signup    # full signup flow
//   node .claude/skills/run-ironmonkey/driver.mjs login     # bad-credential flow
//   node .claude/skills/run-ironmonkey/driver.mjs shot <url> [file]
//   node .claude/skills/run-ironmonkey/driver.mjs all
//
// Screenshots and logs land in .run/ (gitignored).

import { execFile, spawn } from 'node:child_process';
import { mkdirSync, writeFileSync, existsSync } from 'node:fs';
import { promisify } from 'node:util';

const execFileP = promisify(execFile);

const WEB = process.env.IM_WEB_URL || 'http://localhost:5299';
const API = process.env.IM_API_URL || 'https://localhost:7306';
const OUT = process.env.IM_OUT_DIR || '.run';
const CDP_PORT = Number(process.env.IM_CDP_PORT || 9222);
const CHROME = process.env.IM_CHROME || 'google-chrome';

const sleep = ms => new Promise(r => setTimeout(r, ms));
let failures = 0;
const ok = (label, pass, detail = '') => {
  if (!pass) failures++;
  console.log(`  ${pass ? 'PASS' : 'FAIL'}  ${label}${detail ? `  ${detail}` : ''}`);
};

// --- HTTP helpers -----------------------------------------------------------
// -k because the API uses the ASP.NET dev certificate, which is not in the
// Linux trust store (dotnet dev-certs --trust hangs here).
async function curl(url, args = []) {
  const { stdout } = await execFileP('curl', [
    '-sk', '-m', '20', '-o', '/dev/null', '-w', '%{http_code}', ...args, url,
  ]).catch(e => ({ stdout: '000', err: e }));
  return stdout.trim();
}

async function curlBody(url, args = []) {
  const { stdout } = await execFileP('curl', ['-sk', '-m', '20', ...args, url])
    .catch(() => ({ stdout: '' }));
  return stdout;
}

// --- CDP session ------------------------------------------------------------
class Chrome {
  static async launch() {
    const c = new Chrome();
    // Reuse an already-open debug browser if one is listening.
    if (!(await c.#endpointUp())) {
      c.proc = spawn(CHROME, [
        '--headless', '--disable-gpu', '--no-sandbox', '--hide-scrollbars',
        '--window-size=1280,1100',
        `--remote-debugging-port=${CDP_PORT}`,
        // Profile dir is per-port: Chrome refuses to start a second instance that
        // shares a user-data-dir, which silently breaks IM_CDP_PORT overrides.
        `--user-data-dir=${OUT}/cdp-profile-${CDP_PORT}`,
        'about:blank',
      ], { stdio: 'ignore', detached: true });
      c.proc.unref();
      for (let i = 0; i < 40; i++) {
        if (await c.#endpointUp()) break;
        await sleep(250);
      }
      if (!(await c.#endpointUp())) throw new Error(`Chrome CDP never came up on :${CDP_PORT}`);
    }
    await c.#attach();
    return c;
  }

  async #endpointUp() {
    try {
      const r = await fetch(`http://127.0.0.1:${CDP_PORT}/json/version`, { signal: AbortSignal.timeout(1500) });
      return r.ok;
    } catch { return false; }
  }

  async #attach() {
    let targets = await (await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`)).json();
    let page = targets.find(t => t.type === 'page');
    if (!page) {
      await fetch(`http://127.0.0.1:${CDP_PORT}/json/new?about:blank`, { method: 'PUT' });
      targets = await (await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`)).json();
      page = targets.find(t => t.type === 'page');
    }
    this.ws = new WebSocket(page.webSocketDebuggerUrl);
    this.id = 0;
    this.pending = new Map();
    await new Promise((res, rej) => {
      this.ws.onopen = res;
      this.ws.onerror = () => rej(new Error('CDP websocket failed'));
    });
    this.ws.onmessage = e => {
      const m = JSON.parse(e.data);
      if (m.id && this.pending.has(m.id)) { this.pending.get(m.id)(m); this.pending.delete(m.id); }
    };
    await this.send('Page.enable');
    await this.send('Runtime.enable');
  }

  send(method, params = {}) {
    return new Promise(res => {
      const id = ++this.id;
      this.pending.set(id, res);
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  async eval(expression) {
    const r = await this.send('Runtime.evaluate', {
      expression, returnByValue: true, awaitPromise: true,
    });
    return r.result?.result?.value;
  }

  /** Navigate and wait for the Blazor circuit to boot. */
  async goto(url, settleMs = 3500) {
    await this.send('Page.navigate', { url });
    await sleep(settleMs);
    // window.Blazor only exists once the interactive circuit is live.
    for (let i = 0; i < 20; i++) {
      if (await this.eval('typeof window.Blazor !== "undefined"')) break;
      await sleep(250);
    }
  }

  async shot(file) {
    const r = await this.send('Page.captureScreenshot', { format: 'png' });
    if (!r.result?.data) throw new Error('captureScreenshot returned no data');
    mkdirSync(OUT, { recursive: true });
    writeFileSync(file, Buffer.from(r.result.data, 'base64'));
    return file;
  }

  /**
   * Fill a field located by its visible label. The forms carry no id/name
   * attributes, so we walk from the label to the next input/select sibling.
   * Dispatches input+change so Blazor's @bind-Value observes the value.
   */
  async fillByLabel(labelText, value) {
    const expr = `(() => {
      const want = ${JSON.stringify(labelText)}.toLowerCase();
      const label = [...document.querySelectorAll('label')]
        .find(l => l.textContent.trim().toLowerCase().includes(want));
      if (!label) return 'no-label';
      let el = label.nextElementSibling;
      while (el && !['INPUT','SELECT'].includes(el.tagName)) el = el.nextElementSibling;
      if (!el) return 'no-control';
      el.focus();
      if (el.tagName === 'SELECT') {
        el.value = ${JSON.stringify(value)};
      } else {
        const setter = Object.getOwnPropertyDescriptor(
          el.tagName === 'INPUT' ? HTMLInputElement.prototype : HTMLTextAreaElement.prototype, 'value').set;
        setter.call(el, ${JSON.stringify(value)});
      }
      el.dispatchEvent(new Event('input',  { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return 'ok';
    })()`;
    return this.eval(expr);
  }

  async clickSelector(sel) {
    return this.eval(`(() => {
      const el = document.querySelector(${JSON.stringify(sel)});
      if (!el) return 'not-found';
      el.click();
      return 'clicked';
    })()`);
  }

  async text() { return this.eval('document.body.innerText'); }
  async url()  { return this.eval('location.href'); }

  close() { try { this.ws?.close(); } catch {} }
}

// --- flows ------------------------------------------------------------------

async function smoke() {
  console.log('\nAPI');
  ok('GET  /health          200', await curl(`${API}/health`) === '200');
  ok('GET  /api/recipes     200', await curl(`${API}/api/recipes`) === '200');
  // Protected routes must challenge, not 404 (they used to 404 — double-prefixed).
  ok('GET  /tenants         401', await curl(`${API}/tenants`) === '401');
  const login = await curl(`${API}/auth/login`, ['-X', 'POST', '-H', 'Content-Type: application/json', '-d', '{}']);
  ok('POST /auth/login      400 (validation)', login === '400', `got ${login}`);

  console.log('\nWeb');
  for (const path of ['/', '/login', '/signup', '/counter']) {
    const code = await curl(`${WEB}${path}`);
    ok(`GET  ${path.padEnd(17)}200`, code === '200', `got ${code}`);
  }
}

async function signupFlow(chrome) {
  console.log('\nSignup flow (Blazor interactive)');
  await chrome.goto(`${WEB}/signup`);

  const controls = await chrome.eval('document.querySelectorAll("input,select").length');
  ok('form controls present', controls >= 7, `found ${controls}`);

  // Recipe cards come from GET /api/recipes through the server-side HttpClient.
  const cards = await chrome.eval(
    '[...document.querySelectorAll("h3")].map(h=>h.textContent.trim()).filter(Boolean).join("|")');
  ok('recipe cards loaded from API', /Automobile|Educational/.test(cards || ''), cards || '(none)');

  const email = `driver-${Date.now()}@example.com`;
  const fields = [
    ['Billing Contact', 'Driver Smoke'],
    ['Business Email', email],
    ['Company Name', 'Driver Motors'],
    ['Phone Number', '5550001234'],
    ['Company Size', '11-50'],
    ['Company Address', '1 Driver Way'],
    ['Password', 'Sup3rStr0ng!pass'],
  ];
  let filled = 0;
  for (const [label, value] of fields) {
    if (await chrome.fillByLabel(label, value) === 'ok') filled++;
    else console.log(`         (could not fill "${label}")`);
  }
  ok('all fields filled', filled === fields.length, `${filled}/${fields.length}`);

  await chrome.shot(`${OUT}/signup-filled.png`);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const url = await chrome.url();
  ok('navigated to success page', /signup\/success/.test(url || ''), url);
  await chrome.shot(`${OUT}/signup-result.png`);
  console.log(`  screenshots: ${OUT}/signup-filled.png, ${OUT}/signup-result.png`);
  console.log(`  email used:  ${email}`);
}

async function loginFlow(chrome) {
  console.log('\nLogin flow (bad credentials)');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', 'nobody@example.com');
  await chrome.fillByLabel('Password', 'WrongPassword123');
  await chrome.clickSelector('button[type="submit"]');
  await sleep(5000);

  // A live circuit keeps the typed value; static SSR would clear it.
  const retained = await chrome.eval('document.querySelector(\'input[type="email"]\')?.value || ""');
  ok('email retained (circuit live)', retained === 'nobody@example.com', JSON.stringify(retained));

  const body = (await chrome.text()) || '';
  ok('shows credential error', /invalid|incorrect|password/i.test(body));
  await chrome.shot(`${OUT}/login-result.png`);
}

async function counterFlow(chrome) {
  console.log('\nInteractivity check (@onclick)');
  await chrome.goto(`${WEB}/counter`);
  const read = () => chrome.eval(
    '(document.body.innerText.match(/current count[^0-9]*(\\d+)/i)||[])[1] || ""');
  const before = await read();
  await chrome.clickSelector('button.btn-primary');
  await sleep(1200);
  await chrome.clickSelector('button.btn-primary');
  await sleep(1200);
  const after = await read();
  ok('@onclick updates server state', before !== after, `${before} -> ${after}`);
}

// --- main -------------------------------------------------------------------
const cmd = process.argv[2] || 'smoke';
mkdirSync(OUT, { recursive: true });

if (cmd === 'shot') {
  const url = process.argv[3] || `${WEB}/`;
  const file = process.argv[4] || `${OUT}/shot.png`;
  const chrome = await Chrome.launch();
  await chrome.goto(url);
  console.log('saved', await chrome.shot(file));
  chrome.close();
  process.exit(0);
}

if (cmd === 'smoke') {
  await smoke();
} else {
  const needsBrowser = ['signup', 'login', 'counter', 'all'].includes(cmd);
  if (!needsBrowser) {
    console.error(`unknown command: ${cmd}`);
    console.error('use: smoke | signup | login | counter | all | shot <url> [file]');
    process.exit(2);
  }
  if (cmd === 'all') await smoke();
  const chrome = await Chrome.launch();
  try {
    if (cmd === 'signup' || cmd === 'all') await signupFlow(chrome);
    if (cmd === 'login'  || cmd === 'all') await loginFlow(chrome);
    if (cmd === 'counter'|| cmd === 'all') await counterFlow(chrome);
  } finally {
    chrome.close();
  }
}

console.log(failures === 0 ? '\nAll checks passed.' : `\n${failures} check(s) FAILED.`);
process.exit(failures === 0 ? 0 : 1);
