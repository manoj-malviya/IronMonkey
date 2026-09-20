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

// Signs in with real credentials, then screenshots each requested path.
// Usage: driver.mjs signin <email> <password> [path ...]
async function signinFlow(chrome, email, password, paths) {
  console.log(`\nSign-in flow (${email})`);
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const url = await chrome.url() || '';
  const body = (await chrome.text()) || '';
  const failed = /invalid email address or password/i.test(body);
  ok('signed in (no credential error)', !failed, url);
  await chrome.shot(`${OUT}/signin-result.png`);
  console.log(`  screenshot: ${OUT}/signin-result.png  (${url})`);

  for (const path of paths) {
    // Navigate by clicking the in-page link, not chrome.goto: a full reload tears down
    // the SignalR circuit, and the JWT lives in the circuit's memory, so the reloaded
    // page authenticates as anonymous and bounces to /login.
    // Prefer a real <a href>; fall back to a button whose label matches, since some pages
    // are reached only through an @onclick button and never appear in the sidebar.
    const clicked = await chrome.eval(`(() => {
      const a = [...document.querySelectorAll('a')].find(x => (x.getAttribute('href')||'') === '${path}');
      if (a) { a.click(); return 'ok'; }
      const label = ${JSON.stringify('')} || '';
      const want = '${path}'.split('/').pop().replace(/-/g, ' ');
      const b = [...document.querySelectorAll('button')]
        .find(x => (x.textContent||'').toLowerCase().replace(/\s+/g,' ').includes(want));
      if (b) { b.click(); return 'ok'; }
      return 'no-link';
    })()`);
    if (clicked !== 'ok') await chrome.goto(`${WEB}${path}`);
    await sleep(6000);
    const to = await chrome.url() || '';
    const name = path.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'root';
    ok(`loaded ${path}`, !/\/login/.test(to), to);
    await chrome.shot(`${OUT}/signin-${name}.png`);
    console.log(`  screenshot: ${OUT}/signin-${name}.png`);
  }
}

// Exercises the CRM write paths through the real UI: create a contact via the form,
// then convert a lead into a contact + opportunity.
async function crmFlow(chrome, email, password) {
  console.log('\nCRM flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const nav = async (href) => {
    const r = await chrome.eval(`(() => {
      const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${href}');
      if(a){a.click();return 'ok';} return 'no-link';
    })()`);
    await sleep(3000);
    return r;
  };
  const clickText = async (text) => {
    const r = await chrome.eval(`(() => {
      const b=[...document.querySelectorAll('button,a')]
        .find(x=>(x.textContent||'').trim().toLowerCase().includes(${JSON.stringify('')}+'${text}'.toLowerCase()));
      if(b){b.click();return 'ok';} return 'not-found';
    })()`);
    await sleep(3000);
    return r;
  };

  // 1. Create a contact through the form.
  await nav('/admin/contacts');
  ok('opened contacts', /admin\/contacts/.test(await chrome.url() || ''));
  await clickText('New Contact');
  const stamp = Date.now();
  const cEmail = `ui.contact.${stamp}@example.com`;
  await chrome.fillByLabel('Name', `UI Contact ${stamp}`);
  await chrome.fillByLabel('Email', cEmail);
  await chrome.fillByLabel('Phone', '9800000123');
  await chrome.clickSelector('button[type="submit"]');
  await sleep(4000);
  const afterCreate = (await chrome.text()) || '';
  ok('contact appears in list', afterCreate.includes(cEmail), cEmail);
  await chrome.shot(`${OUT}/crm-contacts.png`);

  // 2. Convert a lead into contact + opportunity.
  await nav('/admin/leads');
  const conv = await clickText('Convert');
  ok('opened convert page', conv === 'ok' && /convert/.test(await chrome.url() || ''), await chrome.url());
  await chrome.clickSelector('button[type="submit"]');
  await sleep(5000);
  const url = await chrome.url() || '';
  ok('converted and landed on a CRM page', /opportunities|contacts/.test(url), url);
  await chrome.shot(`${OUT}/crm-after-convert.png`);
}

// Walks the custom-field surface: the definitions tab, and a lead form rendering them.
async function customFieldsFlow(chrome, email, password) {
  console.log('\nCustom fields flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const nav = async (href) => {
    await chrome.eval(`(() => {
      const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${href}');
      if(a){a.click();return 'ok';} return 'no-link';
    })()`);
    await sleep(3000);
  };
  const clickText = async (text) => {
    const r = await chrome.eval(`(() => {
      const b=[...document.querySelectorAll('button,a')]
        .find(x=>(x.textContent||'').trim().toLowerCase()==='${text}'.toLowerCase());
      if(b){b.click();return 'ok';} return 'not-found';
    })()`);
    await sleep(3000);
    return r;
  };

  await nav('/admin/configuration');
  ok('opened system configuration', /configuration/.test(await chrome.url() || ''));
  const tab = await clickText('Custom Fields');
  ok('opened Custom Fields tab', tab === 'ok');
  await chrome.shot(`${OUT}/cf-definitions.png`);

  await nav('/admin/leads');
  await clickText('+ New Lead');
  const body = (await chrome.text()) || '';
  ok('lead form renders the custom field', /Interested Car/i.test(body));
  await chrome.shot(`${OUT}/cf-lead-form.png`);

  await nav('/admin/contacts');
  await clickText('+ New Contact');
  const cbody = (await chrome.text()) || '';
  ok('contact form renders the custom field', /Interested Car/i.test(cbody));
  await chrome.shot(`${OUT}/cf-contact-form.png`);
}

// Fills a lead form including its custom dropdown, saves, and reopens it to confirm the
// value round-tripped through the jsonb column.
async function cfSubmitFlow(chrome, email, password) {
  console.log('\nCustom field submit flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const nav = async (href) => {
    await chrome.eval(`(() => {
      const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${href}');
      if(a){a.click();return 'ok';} return 'no-link';
    })()`);
    await sleep(3000);
  };
  const clickText = async (text) => {
    await chrome.eval(`(() => {
      const b=[...document.querySelectorAll('button,a')]
        .find(x=>(x.textContent||'').trim().toLowerCase()==='${text}'.toLowerCase());
      if(b){b.click();return 'ok';} return 'not-found';
    })()`);
    await sleep(3000);
  };

  await nav('/admin/leads');
  await clickText('+ New Lead');

  const stamp = Date.now();
  const mail = `cf.ui.${stamp}@example.com`;
  await chrome.fillByLabel('First name', 'Custom');
  await chrome.fillByLabel('Last name', `Field${stamp}`);
  await chrome.fillByLabel('Email', mail);
  await chrome.fillByLabel('Phone', '9700000001');

  // The custom dropdown is the last select on the page.
  const picked = await chrome.eval(`(() => {
    const sels=[...document.querySelectorAll('select')];
    const s=sels[sels.length-1];
    if(!s) return 'no-select';
    const opt=[...s.options].find(o=>o.value==='Ciaz');
    if(!opt) return 'no-option';
    s.value='Ciaz';
    s.dispatchEvent(new Event('change',{bubbles:true}));
    return 'ok';
  })()`);
  ok('selected a custom dropdown value', picked === 'ok', String(picked));
  await sleep(1500);

  await chrome.clickSelector('button[type="submit"]');
  await sleep(5000);
  const listBody = (await chrome.text()) || '';
  ok('lead saved with custom field', listBody.includes(mail), mail);

  // Reopen the lead to confirm the value prefills from storage.
  await chrome.eval(`(() => {
    const rows=[...document.querySelectorAll('tr')];
    const row=rows.find(r=>(r.textContent||'').includes('${mail}'));
    if(!row) return 'no-row';
    const edit=[...row.querySelectorAll('button')].find(b=>(b.textContent||'').trim()==='Edit');
    if(edit){edit.click();return 'ok';} return 'no-edit';
  })()`);
  await sleep(4000);
  const selected = await chrome.eval(`(() => {
    const sels=[...document.querySelectorAll('select')];
    const s=sels[sels.length-1];
    return s ? s.value : '';
  })()`);
  ok('custom value round-tripped on edit', selected === 'Ciaz', String(selected));
  await chrome.shot(`${OUT}/cf-roundtrip.png`);
}

// Opens each CRM list, clicks the first record's name to reach its detail page, adds a
// note, and screenshots the timeline.
async function detailFlow(chrome, email, password) {
  console.log('\nCRM detail + timeline flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const nav = async (href) => {
    await chrome.eval(`(() => {
      const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${href}');
      if(a){a.click();return 'ok';} return 'no-link';
    })()`);
    await sleep(3000);
  };

  for (const [label, href] of [['leads','/admin/leads'], ['contacts','/admin/contacts'], ['opportunities','/admin/opportunities']]) {
    await nav(href);
    // The record name is the first link-styled button inside the table body.
    const opened = await chrome.eval(`(() => {
      const b=document.querySelector('tbody button.text-indigo-600');
      if(b){b.click();return 'ok';} return 'no-row';
    })()`);
    await sleep(3500);
    const url = await chrome.url() || '';
    ok(`opened ${label} detail`, opened === 'ok' && /[0-9a-f-]{36}$/.test(url), url);

    const body = (await chrome.text()) || '';
    ok(`${label} shows Activity panel`, body.includes('Activity'));

    // Add a note through the timeline widget.
    const typed = await chrome.eval(`(() => {
      const t=document.querySelector('textarea');
      if(!t) return 'no-textarea';
      const setter=Object.getOwnPropertyDescriptor(window.HTMLTextAreaElement.prototype,'value').set;
      setter.call(t, 'Checked in via UI test.');
      t.dispatchEvent(new Event('input',{bubbles:true}));
      t.dispatchEvent(new Event('change',{bubbles:true}));
      return 'ok';
    })()`);
    if (typed === 'ok') {
      await sleep(800);
      await chrome.eval(`(() => {
        const b=[...document.querySelectorAll('button')].find(x=>(x.textContent||'').trim()==='Add');
        if(b){b.click();return 'ok';} return 'no-add';
      })()`);
      await sleep(4000);
      const after = (await chrome.text()) || '';
      ok(`${label} note appears on timeline`, after.includes('Checked in via UI test.'));
    }

    await chrome.shot(`${OUT}/detail-${label}.png`);
    console.log(`  screenshot: ${OUT}/detail-${label}.png`);
  }
}

// Creates a role through the Roles UI, checks SuperAdmin/admin:access are never offered,
// then edits and deletes it.
async function roleFlow(chrome, email, password) {
  console.log('\nRole management flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  await chrome.eval(`(() => {
    const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='/admin/roles');
    if(a){a.click();return 'ok';} return 'no-link';
  })()`);
  await sleep(6000);

  const listText = (await chrome.text()) || '';
  ok('SuperAdmin not listed', !listText.includes('SuperAdmin'));
  ok('admin:access not listed', !listText.includes('admin:access'));

  // Open the New Role dialog.
  await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>(x.textContent||'').includes('New Role'));
    if(b){b.click();return 'ok';} return 'no-button';
  })()`);
  await sleep(1500);

  const dialogText = (await chrome.text()) || '';
  ok('editor offers no admin permission', !dialogText.includes('admin:access') && !/\badmin\b/i.test(dialogText.split('Role name')[1] || ''));

  const roleName = `UI Role ${Date.now()}`;
  await chrome.eval(`(() => {
    const i=document.querySelector('input[type="text"]');
    if(!i) return 'no-input';
    const setter=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
    setter.call(i, ${JSON.stringify(roleName)});
    i.dispatchEvent(new Event('input',{bubbles:true}));
    i.dispatchEvent(new Event('change',{bubbles:true}));
    return 'ok';
  })()`);
  await sleep(600);

  // Tick the first two permission checkboxes.
  await chrome.eval(`(() => {
    const boxes=[...document.querySelectorAll('input[type="checkbox"]')].slice(0,2);
    boxes.forEach(b => { b.click(); });
    return boxes.length;
  })()`);
  await sleep(800);

  await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>(x.textContent||'').trim().startsWith('Save Role'));
    if(b){b.click();return 'ok';} return 'no-save';
  })()`);
  await sleep(5000);

  const afterCreate = (await chrome.text()) || '';
  ok('custom role appears in list', afterCreate.includes(roleName), roleName);
  await chrome.shot(`${OUT}/roles-created.png`);

  // Delete it again so the tenant is left as found.
  const deleted = await chrome.eval(`(() => {
    const rows=[...document.querySelectorAll('tbody tr')];
    const row=rows.find(r => (r.textContent||'').includes('UI Role'));
    if(!row) return 'no-row';
    const b=[...row.querySelectorAll('button')].find(x=>(x.textContent||'').trim()==='Delete');
    if(b){b.click();return 'ok';} return 'no-delete';
  })()`);
  await sleep(1200);
  if (deleted === 'ok') {
    await chrome.eval(`(() => {
      const b=[...document.querySelectorAll('button')].filter(x=>(x.textContent||'').trim()==='Delete').pop();
      if(b){b.click();return 'ok';} return 'no-confirm';
    })()`);
    await sleep(4000);
    const stillInTable = await chrome.eval(`(() => {
      return [...document.querySelectorAll('tbody tr')].some(r => (r.textContent||'').includes('UI Role'));
    })()`);
    ok('custom role removed from table', stillInTable === false, String(stillInTable));
  }
  await chrome.shot(`${OUT}/roles-final.png`);
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


// Drives the configuration workspace: the stage side panel, the accessible reorder controls
// with their unsaved/saved states, the custom-field editor's live preview, and the
// impact-aware remove dialogs. These are the parts a compile cannot prove work.
async function configFlow(chrome, email, password) {
  console.log('\nConfiguration workspace flow');
  await chrome.goto(`${WEB}/login`);
  await chrome.fillByLabel('Email', email);
  await chrome.fillByLabel('Password', password);
  await chrome.clickSelector('button[type="submit"]');
  await sleep(6000);

  const clickText = async (text) => {
    const r = await chrome.eval(`(() => {
      const b=[...document.querySelectorAll('button,a')]
        .find(x=>(x.textContent||'').trim().toLowerCase()===${JSON.stringify('')}+'${text}'.toLowerCase());
      if(b){b.click();return 'ok';} return 'not-found';
    })()`);
    await sleep(2500);
    return r;
  };
  const clickAria = async (label) => {
    const r = await chrome.eval(`(() => {
      const b=document.querySelector('[aria-label="${label}"]');
      if(b){b.click();return 'ok';} return 'not-found';
    })()`);
    await sleep(2500);
    return r;
  };

  // Navigate in-page: chrome.goto reloads, which tears down the SignalR circuit holding
  // the JWT and bounces to /login. Blazor intercepts internal <a href> clicks, so the tab
  // links route client-side and the session survives.
  const navHref = async (href) => {
    const r = await chrome.eval(`(() => {
      const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${href}');
      if(a){a.click();return 'ok';} return 'no-link';
    })()`);
    await sleep(4000);
    return r;
  };

  // --- Pipeline stages ----------------------------------------------------
  await navHref('/admin/configuration');
  await sleep(2000);

  let body = (await chrome.text()) || '';
  const firstStage = (body.match(/1\s*\n\s*(\w[\w ]*)/) || [])[1] || '';
  ok('stages listed', /Add stage/.test(body), firstStage && `first: ${firstStage}`);

  // Reorder via the keyboard-accessible control, then confirm the unsaved state appears.
  const moved = await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>/^Move .* down$/.test(x.getAttribute('aria-label')||''));
    if(b){b.click();return b.getAttribute('aria-label');} return 'not-found';
  })()`);
  await sleep(1500);
  body = (await chrome.text()) || '';
  ok('move control marks order unsaved', /Unsaved order changes/.test(body), String(moved));

  await clickText('Discard');
  body = (await chrome.text()) || '';
  ok('discard clears the unsaved state', !/Unsaved order changes/.test(body));

  // The edit panel must open with the stage's real name in it.
  await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>/^Edit /.test(x.getAttribute('aria-label')||''));
    if(b)b.click();
  })()`);
  await sleep(2500);
  const panelName = await chrome.eval(`(document.querySelector('#stage-name')||{}).value || ''`);
  ok('stage panel opens prefilled', !!panelName, `name: "${panelName}"`);
  await chrome.shot(`${OUT}/config-stage-panel.png`);
  await clickAria('Close');

  // Remove must report the real lead count before it will proceed.
  await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>/^Remove /.test(x.getAttribute('aria-label')||''));
    if(b)b.click();
  })()`);
  await sleep(3500);
  body = (await chrome.text()) || '';
  ok('remove dialog states the impact', /is used by/.test(body) || /only active stage/.test(body));
  await chrome.shot(`${OUT}/config-stage-impact.png`);
  await clickText('Cancel');

  // --- Custom fields ------------------------------------------------------
  ok('fields tab link routes in-page', await navHref('/admin/configuration?tab=fields') === 'ok');
  body = (await chrome.text()) || '';
  ok('fields tab deep-links', /Add field/.test(body));

  await clickText('Add field');
  const labelSet = await chrome.eval(`(() => {
    const i=document.querySelector('#f-label');
    if(!i) return 'no-input';
    const s=Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
    s.call(i,'Driver Probe Field');
    i.dispatchEvent(new Event('input',{bubbles:true}));
    return 'ok';
  })()`);
  await sleep(2000);
  const autoKey = await chrome.eval(`(document.querySelector('#f-key')||{}).placeholder || ''`);
  ok('internal key is derived from the label', autoKey === 'driver_probe_field', `placeholder: "${autoKey}"`);

  // Switching to Dropdown must reveal repeatable option rows, not a comma box.
  await chrome.eval(`(() => {
    const sel=document.querySelector('#f-type');
    if(!sel) return 'no-select';
    const s=Object.getOwnPropertyDescriptor(window.HTMLSelectElement.prototype,'value').set;
    s.call(sel,'Dropdown');
    sel.dispatchEvent(new Event('change',{bubbles:true}));
    return 'ok';
  })()`);
  await sleep(2500);
  const optionRows = await chrome.eval(
    `document.querySelectorAll('[aria-label^="Option "]').length`);
  ok('dropdown shows repeatable option inputs', Number(optionRows) >= 2, `${optionRows} rows`);

  const previewLabel = await chrome.eval(`(() => {
    const h=[...document.querySelectorAll('p')].find(x=>/^preview$/i.test((x.textContent||'').trim()));
    if(!h) return 'no-preview';
    // The preview renders the label being typed, which is what proves it is live.
    return (h.parentElement.textContent||'').includes('Driver Probe Field') ? 'live' : 'static';
  })()`);
  ok('preview renders the field being configured', previewLabel === 'live', String(previewLabel));
  await chrome.shot(`${OUT}/config-field-editor.png`);
  await clickAria('Close');

  // Removing a field that holds data must offer archiving, not deletion.
  await chrome.eval(`(() => {
    const b=[...document.querySelectorAll('button')].find(x=>/^Remove /.test(x.getAttribute('aria-label')||''));
    if(b)b.click();
  })()`);
  await sleep(3500);
  body = (await chrome.text()) || '';
  ok('field remove dialog explains archiving', /archived/i.test(body) || /can be deleted outright/i.test(body));
  await chrome.shot(`${OUT}/config-field-impact.png`);
  await clickText('Cancel');

  // --- Routing ------------------------------------------------------------
  ok('routing tab link routes in-page', await navHref('/admin/configuration?tab=routing') === 'ok');
  await chrome.eval(`(() => {
    const r=[...document.querySelectorAll('input[type=radio]')].find(x=>x.value==='Territory');
    if(r){r.click();return 'ok';} return 'not-found';
  })()`);
  await sleep(3000);
  body = (await chrome.text()) || '';
  ok('territory editor replaces raw JSON', /Territory rules/.test(body) && /Advanced JSON/.test(body));
  await chrome.shot(`${OUT}/config-routing.png`);

  // --- Form integration ----------------------------------------------------
  // The point of configuring help text and a default is that the lead form honours them.
  await navHref('/admin/leads');
  await sleep(3000);
  await clickText('+ New Lead');
  await sleep(4000);
  body = (await chrome.text()) || '';
  ok('lead form shows the configured help text', /Model the customer asked about/.test(body));
  const defaulted = await chrome.eval(`(() => {
    const sels=[...document.querySelectorAll('select')];
    const s=sels.find(x=>[...x.options].some(o=>o.value==='Swift'));
    return s ? s.value : 'no-select';
  })()`);
  ok('lead form pre-fills the configured default', defaulted === 'Swift', String(defaulted));
  await chrome.shot(`${OUT}/config-lead-form.png`);

  // --- Mobile width --------------------------------------------------------
  // The acceptance bar is that the primary task never needs sideways scrolling, so measure
  // the document against the viewport rather than eyeballing the screenshot.
  await chrome.send('Emulation.setDeviceMetricsOverride',
    { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  await sleep(1500);

  for (const [tab, href] of [['stages', '/admin/configuration'],
                             ['fields', '/admin/configuration?tab=fields']]) {
    await navHref(href);
    await sleep(2500);
    const overflow = await chrome.eval(
      `document.documentElement.scrollWidth - document.documentElement.clientWidth`);
    ok(`no horizontal scroll at 390px (${tab})`, Number(overflow) <= 1, `overflow ${overflow}px`);
    await chrome.shot(`${OUT}/config-mobile-${tab}.png`);
  }

  await chrome.send('Emulation.clearDeviceMetricsOverride', {});
}

// --- main -------------------------------------------------------------------
const cmd = process.argv[2] || 'smoke';
mkdirSync(OUT, { recursive: true });

if (cmd === 'scrollshot') {
  const url = process.argv[3];
  const file = process.argv[4] || `${OUT}/scroll.png`;
  const y = parseInt(process.argv[5] || '900', 10);
  const chrome = await Chrome.launch();
  const [, , , , , email, password] = process.argv;
  if (email) {
    await chrome.goto(`${WEB}/login`);
    await chrome.fillByLabel('Email', email);
    await chrome.fillByLabel('Password', password);
    await chrome.clickSelector('button[type="submit"]');
    await sleep(6000);
    await chrome.eval(`(() => { const a=[...document.querySelectorAll('a')].find(x=>(x.getAttribute('href')||'')==='${url}'); if(a){a.click();return 'ok';} return 'no'; })()`);
    await sleep(5000);
  } else {
    await chrome.goto(`${WEB}${url}`);
  }
  await chrome.eval(`window.scrollTo(0, ${y})`);
  await sleep(1200);
  console.log('saved', await chrome.shot(file));
  chrome.close();
  process.exit(0);
}

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
  const needsBrowser = ['signup', 'login', 'counter', 'all', 'signin', 'crmflow', 'customfields', 'cfsubmit', 'detailflow', 'roleflow', 'configflow'].includes(cmd);
  if (!needsBrowser) {
    console.error(`unknown command: ${cmd}`);
    console.error('use: smoke | signup | login | counter | signin <email> <pw> [path ...] | configflow <email> <pw> | all | shot <url> [file]');
    process.exit(2);
  }
  if (cmd === 'all') await smoke();
  const chrome = await Chrome.launch();
  try {
    if (cmd === 'signup' || cmd === 'all') await signupFlow(chrome);
    if (cmd === 'login'  || cmd === 'all') await loginFlow(chrome);
    if (cmd === 'counter'|| cmd === 'all') await counterFlow(chrome);
    if (cmd === 'cfsubmit') {
      const [, , , email, password] = process.argv;
      await cfSubmitFlow(chrome, email, password);
    }
    if (cmd === 'customfields') {
      const [, , , email, password] = process.argv;
      await customFieldsFlow(chrome, email, password);
    }
    if (cmd === 'roleflow') {
      const [, , , email, password] = process.argv;
      await roleFlow(chrome, email, password);
    }
    if (cmd === 'detailflow') {
      const [, , , email, password] = process.argv;
      await detailFlow(chrome, email, password);
    }
    if (cmd === 'crmflow') {
      const [, , , email, password] = process.argv;
      await crmFlow(chrome, email, password);
    }
    if (cmd === 'configflow') {
      const [, , , email, password] = process.argv;
      await configFlow(chrome, email, password);
    }
    if (cmd === 'signin') {
      const [, , , email, password, ...paths] = process.argv;
      if (!email || !password) {
        console.error('signin needs: driver.mjs signin <email> <password> [path ...]');
        process.exit(2);
      }
      await signinFlow(chrome, email, password, paths);
    }
  } finally {
    chrome.close();
  }
}

console.log(failures === 0 ? '\nAll checks passed.' : `\n${failures} check(s) FAILED.`);
process.exit(failures === 0 ? 0 : 1);
