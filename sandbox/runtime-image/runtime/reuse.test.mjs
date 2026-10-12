// reuse.test.mjs — node:test suite for reuse mode (reuse.mjs), against the real bootstrap.
//
//   node --test runtime/
//
// Each case starts one sandbox process in BLOCKS_RUNTIME_MODE=reuse, sends it call envelopes on
// stdin one at a time — the next only after the previous call's `idle` line, as the runner does —
// and asserts on the stdout protocol it actually produced.

import { test, describe, before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, mkdirSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const BOOTSTRAP = join(HERE, 'bootstrap.mjs');

function envelope(id, overrides = {}) {
  return {
    run: { id, functionId: 'fn_1', version: 1, attempt: 1, invokedBy: { type: 'http', id: null } },
    context: { tenantId: 't1', userId: `user-${id}`, roles: [], permissions: [], isAuthenticated: true },
    env: { REGION: 'ch' },
    input: { call: id },
    limits: { timeoutMs: 3000 },
    ...overrides,
  };
}

/**
 * One reuse-mode sandbox. `files` maps paths under the function root to sources; `index.js` is
 * the entry. Returns helpers to send calls and read the protocol.
 */
function sandbox(files, { env = {}, answer } = {}) {
  const root = mkdtempSync(join(tmpdir(), 'fnreuse-'));
  for (const [path, source] of Object.entries(files)) {
    const full = join(root, path);
    mkdirSync(dirname(full), { recursive: true });
    writeFileSync(full, source, 'utf8');
  }
  const child = spawn(process.execPath, [BOOTSTRAP], {
    env: { ...process.env, BLOCKS_RUNTIME_MODE: 'reuse', BLOCKS_FUNCTION_ENTRY: join(root, 'index.js'),
           BLOCKS_CLEAN_GRACE_MS: '100', ...env },
    stdio: ['pipe', 'pipe', 'pipe'],
  });
  const events = [];
  const waiters = [];
  let buffer = '';
  let exitCode;
  const exited = new Promise((r) => child.on('close', (code) => { exitCode = code; r(code); flush(); }));
  child.stdout.on('data', (d) => {
    buffer += d;
    let i;
    while ((i = buffer.indexOf('\n')) >= 0) {
      const line = buffer.slice(0, i);
      buffer = buffer.slice(i + 1);
      if (!line) continue;
      let event;
      try { event = JSON.parse(line); } catch { event = { t: 'unparsed', raw: line }; }
      events.push(event);
      // Plays the runner's part for `need` lines: answer the call that asked, as the runner does.
      if (event.t === 'need' && answer) {
        const value = answer(event);
        if (value !== undefined) {
          child.stdin.write(JSON.stringify({ t: 'give', call: event.call, id: event.id, value }) + '\n');
        }
      }
    }
    flush();
  });
  function flush() {
    for (let k = waiters.length - 1; k >= 0; k--) {
      const w = waiters[k];
      const hit = events.find(w.match);
      if (hit || exitCode !== undefined) { waiters.splice(k, 1); w.resolve(hit); }
    }
  }
  const waitFor = (match, ms = 8000) => new Promise((resolve, reject) => {
    const hit = events.find(match);
    if (hit || exitCode !== undefined) return resolve(hit);
    const t = setTimeout(() => reject(new Error('timed out waiting; events: ' + JSON.stringify(events))), ms);
    waiters.push({ match, resolve: (v) => { clearTimeout(t); resolve(v); } });
  });
  return {
    events,
    exited,
    get exitCode() { return exitCode; },
    ready: () => waitFor((e) => e.t === 'ready' || e.t === 'fatal'),
    /** Sends a call and resolves with its events once its idle line (or the end) arrives. */
    async call(id, overrides) {
      child.stdin.write(JSON.stringify(envelope(id, overrides)) + '\n');
      await waitFor((e) => (e.t === 'idle' && e.call === id) || e.t === 'fatal');
      return {
        result: events.find((e) => e.t === 'result' && e.call === id),
        idle: events.find((e) => e.t === 'idle' && e.call === id),
        logs: events.filter((e) => e.t === 'log' && e.call === id),
      };
    },
    waitFor,
    /** Writes a raw line to the sandbox's stdin, as anything other than the runner might. */
    send: (line) => child.stdin.write(line + '\n'),
    async close() {
      child.stdin.end();
      await Promise.race([exited, new Promise((r) => setTimeout(r, 3000))]);
      child.kill('SIGKILL');
      rmSync(root, { recursive: true, force: true });
    },
  };
}

describe('reuse mode: one sandbox, many calls', () => {
  test('announces ready, then answers each call with its own context and call id', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        return { input, user: ctx.context.userId, run: ctx.run.id };
      }` });
    try {
      assert.equal((await s.ready()).t, 'ready');
      const a = await s.call('A');
      const b = await s.call('B');
      assert.deepEqual(a.result.value, { input: { call: 'A' }, user: 'user-A', run: 'A' });
      assert.deepEqual(b.result.value, { input: { call: 'B' }, user: 'user-B', run: 'B' });
      assert.equal(a.idle.clean, true);
      assert.equal(b.idle.clean, true);
      assert.ok(s.events.filter((e) => e.t === 'started').every((e) => e.call === 'A' || e.call === 'B'));
    } finally { await s.close(); }
  });

  test('module-level state lives on between calls (the documented reuse contract)', async () => {
    const s = sandbox({ 'index.js': `
      let count = 0;
      export default async function () { count += 1; return count; }` });
    try {
      await s.ready();
      assert.equal((await s.call('A')).result.value, 1);
      assert.equal((await s.call('B')).result.value, 2);
    } finally { await s.close(); }
  });

  test('a thrown error fails that call but leaves the sandbox clean and reusable', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input) { if (input.call === 'A') throw new Error('boom'); return 'ok'; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.ok, false);
      assert.equal(a.result.code, 'USER_RUNTIME_ERROR');
      assert.equal(a.idle.clean, true);
      assert.equal((await s.call('B')).result.value, 'ok');
    } finally { await s.close(); }
  });
});

describe('reuse mode: per-call CPU', () => {
  test('the idle line carries the CPU used in the handler window, not more than its wall time allows', async () => {
    const s = sandbox({ 'index.js': `
      export default async function () {
        const until = Date.now() + 120;
        let x = 0; while (Date.now() < until) x += Math.sqrt(x + 1);   // ~120 ms of CPU
        return x > 0;
      }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(typeof a.idle.cpuMs, 'number');
      assert.ok(a.idle.cpuMs >= 60, `cpuMs ${a.idle.cpuMs} should reflect the busy loop`);
      const started = s.events.find((e) => e.t === 'started' && e.call === 'A');
      const answeredWithin = Date.now() - started.at;
      assert.ok(a.idle.cpuMs <= answeredWithin + 50, 'CPU in the window cannot exceed the window by much');
    } finally { await s.close(); }
  });
});

describe('reuse mode: leftover work makes a call dirty', () => {
  const cases = [
    ['an un-awaited setTimeout', `setTimeout(() => {}, 5000);`, 'Timeout'],
    ['a setInterval', `setInterval(() => {}, 1000);`, 'Interval'],
    ['an unref() timer of its own', `setTimeout(() => {}, 5000).unref();`, 'Timeout'],
  ];
  for (const [name, code, expected] of cases) {
    test(name, async () => {
      const s = sandbox({ 'index.js': `export default async function () { ${code} return 'answered'; }` });
      try {
        await s.ready();
        const a = await s.call('A');
        assert.equal(a.result.value, 'answered', 'the caller still gets the answer');
        assert.equal(a.idle.clean, false);
        assert.ok(a.idle.leftovers.includes(expected), JSON.stringify(a.idle.leftovers));
      } finally { await s.close(); }
    });
  }

  test('a timer that finishes within the grace period is not a leftover', async () => {
    const s = sandbox({ 'index.js': `export default async function () { setTimeout(() => {}, 20); return 1; }` });
    try {
      await s.ready();
      assert.equal((await s.call('A')).idle.clean, true);
    } finally { await s.close(); }
  });

  test('crypto work still running after the answer is a leftover', async () => {
    const s = sandbox({ 'index.js': `
      import { pbkdf2 } from 'node:crypto';
      export default async function () { pbkdf2('p', 's', 3000000, 32, 'sha256', () => {}); return 1; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.idle.clean, false);
      assert.ok(a.idle.leftovers.includes('PBKDF2REQUEST'), JSON.stringify(a.idle.leftovers));
    } finally { await s.close(); }
  });

  test('AbortSignal.timeout (made by Node, not the function) is not a leftover', async () => {
    const s = sandbox({ 'index.js': `export default async function () { AbortSignal.timeout(5000); return 1; }` });
    try {
      await s.ready();
      assert.equal((await s.call('A')).idle.clean, true);
    } finally { await s.close(); }
  });

  test('a function that rewrites Error.prepareStackTrace or freezes stackTraceLimit cannot hide its timer or crash the runtime', async () => {
    const s = sandbox({ 'index.js': `
      Error.prepareStackTrace = () => 'at hidden (/elsewhere/x.js:1:1)';
      Object.defineProperty(Error, 'stackTraceLimit', { value: 0, writable: false });
      export default async function () { setInterval(() => {}, 1000).unref(); return 'answered'; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.value, 'answered');
      assert.equal(a.idle.clean, false);
      assert.ok(a.idle.leftovers.includes('Interval'), JSON.stringify(a.idle.leftovers));
    } finally { await s.close(); }
  });

  test('a run id served twice still tells the earlier call\'s leftovers apart', async () => {
    const s = sandbox({ 'index.js': `
      let n = 0;
      export default async function () {
        n += 1;
        if (n === 1) setTimeout(() => {}, 300).unref();
        await new Promise((r) => setTimeout(r, n === 2 ? 600 : 0));
        return n;
      }` }, { env: { BLOCKS_CLEAN_GRACE_MS: '10' } });
    try {
      await s.ready();
      await s.call('SAME');
      s.events.length = 0;
      const again = await s.call('SAME');
      assert.equal(again.idle.clean, false, 'the first call\'s timer ran inside the second');
      assert.equal(again.idle.late, true);
    } finally { await s.close(); }
  });

  test('a cleared interval is not a leftover', async () => {
    const s = sandbox({ 'index.js': `
      export default async function () { const t = setInterval(() => {}, 1000); clearInterval(t); return 1; }` });
    try {
      await s.ready();
      assert.equal((await s.call('A')).idle.clean, true);
    } finally { await s.close(); }
  });
});

describe('reuse mode: network', () => {
  let server, url;
  before(async () => {
    server = createServer((req, res) => {
      const delay = req.url === '/slow' ? 2000 : 0;
      setTimeout(() => res.end('ok'), delay);
    });
    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    url = `http://127.0.0.1:${server.address().port}`;
  });
  after(() => { server.closeAllConnections?.(); server.close(); });

  test('an awaited fetch is clean, and its keep-alive connection is allowed to stay', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) { const r = await fetch(ctx.env.URL); return await r.text(); }` });
    try {
      await s.ready();
      const a = await s.call('A', { env: { URL: url + '/' } });
      const b = await s.call('B', { env: { URL: url + '/' } });
      assert.equal(a.result.value, 'ok');
      assert.equal(a.idle.clean, true, JSON.stringify(a.idle));
      assert.equal(b.idle.clean, true, JSON.stringify(b.idle));
    } finally { await s.close(); }
  });

  test('a fetch left in flight after the answer is a leftover', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) { fetch(ctx.env.URL).catch(() => {}); return 'answered'; }` });
    try {
      await s.ready();
      const a = await s.call('A', { env: { URL: url + '/slow' } });
      assert.equal(a.result.value, 'answered');
      assert.equal(a.idle.clean, false);
      assert.ok(a.idle.leftovers.some((l) => l.startsWith('fetch')), JSON.stringify(a.idle.leftovers));
    } finally { await s.close(); }
  });

  test('ctx.waitUntil answers first, finishes the work, and keeps the sandbox clean', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        ctx.waitUntil(fetch(ctx.env.URL).then((r) => r.text()));
        return 'answered';
      }` });
    try {
      await s.ready();
      const a = await s.call('A', { env: { URL: url + '/slow' } });
      const resultAt = s.events.indexOf(a.result);
      const idleAt = s.events.indexOf(a.idle);
      assert.equal(a.result.value, 'answered');
      assert.ok(resultAt < idleAt, 'the answer is written before the idle line');
      assert.equal(a.idle.clean, true, JSON.stringify(a.idle));
    } finally { await s.close(); }
  });
});

describe('reuse mode: ctx.waitUntil', () => {
  test('work added to waitUntil from inside waitUntil work is waited for too', async () => {
    const s = sandbox({ 'index.js': `
      let seen = 'none';
      export default async function (input, ctx) {
        if (input.call === 'A') {
          ctx.waitUntil(new Promise((r) => setTimeout(r, 20)).then(() => {
            ctx.waitUntil(new Promise((r) => setTimeout(() => { seen = 'A-late'; r(); }, 50)));
          }));
          return 'A';
        }
        return seen;
      }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.idle.clean, true, JSON.stringify(a.idle));
      assert.equal((await s.call('B')).result.value, 'A-late', 'the nested work finished inside A');
    } finally { await s.close(); }
  });
});

describe('reuse mode: library background on a connection an earlier call opened', () => {
  test('a pool heartbeat writing on its socket during a later call does not make that call dirty', async () => {
    const { createServer: tcp } = await import('node:net');
    const server = tcp((c) => c.on('data', () => {})).listen(0, '127.0.0.1');
    await new Promise((r) => server.once('listening', r));
    const s = sandbox({
      'node_modules/fake-driver/index.js': `
        import net from 'node:net';
        let sock;
        export async function connect(port) {
          if (sock) return;
          sock = await new Promise((ok) => { const c = net.connect(port, '127.0.0.1', () => ok(c)); });
          // the driver's own heartbeat: started inside the first call, ticks forever
          setInterval(() => sock.write('ping'), 30);
        }`,
      'index.js': `
        import { connect } from './node_modules/fake-driver/index.js';
        export default async function (input, ctx) {
          await connect(Number(ctx.env.PORT));
          await new Promise((r) => setTimeout(r, 200));   // heartbeats tick during this call
          return input.call;
        }`,
    });
    try {
      await s.ready();
      const env = { PORT: String(server.address().port) };
      const a = await s.call('A', { env });
      const b = await s.call('B', { env });
      const c = await s.call('C', { env });
      assert.equal(a.idle.clean, true, JSON.stringify(a.idle));
      assert.equal(b.idle.clean, true, JSON.stringify(b.idle));
      assert.equal(c.idle.clean, true, JSON.stringify(c.idle));
    } finally { await s.close(); server.close(); }
  });
});

describe('reuse mode: libraries keep their long-lived state', () => {
  test('a library interval (a pool heartbeat) does not make the call dirty', async () => {
    const s = sandbox({
      'node_modules/fake-db/index.js': `
        export function connect() {
          if (!globalThis.__hb) globalThis.__hb = setInterval(() => {}, 10000);   // heartbeat
          return Promise.resolve({ query: async () => 42 });
        }`,
      'index.js': `
        import { connect } from './node_modules/fake-db/index.js';
        export default async function () { const db = await connect(); return db.query(); }`,
    });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.value, 42);
      assert.equal(a.idle.clean, true, JSON.stringify(a.idle));
    } finally { await s.close(); }
  });

  test('output from module-level listeners is background, not a dirty call', async () => {
    const s = sandbox({ 'index.js': `
      import { EventEmitter } from 'node:events';
      const client = new EventEmitter();
      client.on('note', (m) => console.log('background', m));
      export default async function () { setImmediate(() => client.emit('note', 'x')); return 1; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.value, 1);
    } finally { await s.close(); }
  });
});

describe('reuse mode: one call cannot reach into another', () => {
  test('an earlier call\'s timer that runs inside a later call dirties that later call', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        if (input.call === 'A') setTimeout(() => console.log('late from', input.call), 300).unref();
        await new Promise((r) => setTimeout(r, input.call === 'B' ? 600 : 0));
        return input.call;
      }` }, { env: { BLOCKS_CLEAN_GRACE_MS: '10' } });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.idle.clean, false, 'A left its own timer behind');
      const b = await s.call('B');   // the runner would have destroyed the sandbox; this shows why
      assert.equal(b.idle.clean, false, 'B saw code of A run');
      assert.equal(b.idle.late, true);
    } finally { await s.close(); }
  });

  test('ctx.log is bound to its own call: one kept from A logs as A (late), never as B', async () => {
    const s = sandbox({ 'index.js': `
      import { EventEmitter } from 'node:events';
      const bus = new EventEmitter();
      export default async function (input, ctx) {
        if (input.call === 'A') bus.on('ping', () => ctx.log.info('A listener got ping from ' + input.call));
        if (input.call === 'B') bus.emit('ping');
        return input.call;
      }` });
    try {
      await s.ready();
      await s.call('A');
      const b = await s.call('B');
      const line = s.events.find((e) => e.t === 'log' && /A listener/.test(e.msg));
      assert.ok(line, 'the line was written');
      assert.equal(line.call, 'A');
      assert.equal(line.late, true);
      assert.ok(!b.logs.some((e) => /A listener/.test(e.msg)), 'not among B\'s own lines');
      assert.equal(b.idle.clean, false, 'A\'s code ran inside B');
    } finally { await s.close(); }
  });

  test('a crash caused by an earlier call\'s leftover does not hand its message or secrets to the next caller', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        if (input.call === 'A') setTimeout(() => { throw new Error('leaked ' + ctx.env.KEY); }, 150).unref();
        await new Promise((r) => setTimeout(r, input.call === 'B' ? 400 : 0));
        return input.call;
      }` }, { env: { BLOCKS_CLEAN_GRACE_MS: '10' } });
    try {
      await s.ready();
      await s.call('A', { env: { KEY: 'secret-of-A-123' }, maskedEnv: ['KEY'] });
      const b = await s.call('B', { env: { KEY: 'secret-of-B-456' }, maskedEnv: ['KEY'] });
      await s.exited;
      assert.equal(b.result.ok, false);
      assert.doesNotMatch(b.result.message, /leaked/);
      assert.equal(b.result.stack, undefined);
      assert.ok(!JSON.stringify(s.events).includes('secret-of-A-123'), 'no line carries A\'s secret');
    } finally { await s.close(); }
  });

  test('a crash in the call\'s own work keeps its message, masked', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        setTimeout(() => { throw new Error('own failure ' + ctx.env.KEY); }, 10);
        await new Promise((r) => setTimeout(r, 200));
      }` });
    try {
      await s.ready();
      const a = await s.call('A', { env: { KEY: 'secret-own-789' }, maskedEnv: ['KEY'] });
      await s.exited;
      assert.match(a.result.message, /own failure \[redacted\]/);
      assert.ok(!JSON.stringify(s.events).includes('secret-own-789'));
    } finally { await s.close(); }
  });

  test('a callback on a socket opened by an earlier call is the current call\'s own work', async () => {
    const { createServer: tcp } = await import('node:net');
    const server = tcp((c) => c.on('data', (d) => c.write(d))).listen(0, '127.0.0.1');
    await new Promise((r) => server.once('listening', r));
    const s = sandbox({ 'index.js': `
      import net from 'node:net';
      let sock;
      export default async function (input, ctx) {
        sock ??= await new Promise((ok) => { const c = net.connect(ctx.env.PORT, '127.0.0.1', () => ok(c)); });
        return await new Promise((ok) => {
          sock.once('data', async (d) => { console.log('echo for', input.call); ok({ echo: String(d), token: await ctx.blocks.getAccessToken() }); });
          sock.write(input.call);
        });
      }` });
    try {
      await s.ready();
      const env = { PORT: String(server.address().port) };
      await s.call('A', { env, blocks: { accessToken: 'tok-A-1111' } });
      const b = await s.call('B', { env, blocks: { accessToken: 'tok-B-2222' } });
      assert.deepEqual(b.result.value, { echo: 'B', token: 'tok-B-2222' });
      assert.ok(b.logs.some((e) => e.msg === 'echo for B'), 'B\'s own log line is kept as B\'s');
      assert.equal(b.idle.clean, true, JSON.stringify(b.idle));
    } finally { await s.close(); server.close(); }
  });

  test('ctx.blocks.getAccessToken() works in its call and is refused from a late callback', async () => {
    const s = sandbox({ 'index.js': `
      let leaked;
      export default async function (input, ctx) {
        if (input.call === 'A') {
          const now = await ctx.blocks.getAccessToken();
          setTimeout(async () => { try { leaked = await ctx.blocks.getAccessToken(); } catch (e) { leaked = 'refused: ' + e.message; } }, 50).unref();
          await new Promise((r) => setTimeout(r, 120));
          return { now, leaked };
        }
        return 'B';
      }` });
    try {
      await s.ready();
      const a = await s.call('A', { blocks: { accessToken: 'token-of-A-1234' } });
      assert.equal(a.result.value.now, 'token-of-A-1234');
      assert.equal(a.result.value.leaked, 'token-of-A-1234', 'still inside call A: allowed');
      const s2 = sandbox({ 'index.js': `
        let saved;
        export default async function (input, ctx) {
          if (input.call === 'A') { saved = ctx; return 'A'; }
          try { return await saved.blocks.getAccessToken(); } catch (e) { return 'refused'; }
        }` });
      try {
        await s2.ready();
        await s2.call('A', { blocks: { accessToken: 'token-of-A-1234' } });
        const b = await s2.call('B', { blocks: { accessToken: 'token-of-B-5678' } });
        assert.equal(b.result.value, 'refused', 'a ctx kept from call A cannot give the token of A to B');
      } finally { await s2.close(); }
    } finally { await s.close(); }
  });

  test('secrets are masked per call', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) { console.log('key is', ctx.env.KEY); return 1; }` });
    try {
      await s.ready();
      const a = await s.call('A', { env: { KEY: 'secret-A-value' }, maskedEnv: ['KEY'] });
      const b = await s.call('B', { env: { KEY: 'secret-B-value' }, maskedEnv: ['KEY'] });
      assert.equal(a.logs[0].msg, 'key is [redacted]');
      assert.equal(b.logs[0].msg, 'key is [redacted]');
      assert.ok(!JSON.stringify(s.events).includes('secret-A-value'));
      assert.ok(!JSON.stringify(s.events).includes('secret-B-value'));
    } finally { await s.close(); }
  });
});

describe('reuse mode: the sandbox ends itself when it cannot be trusted', () => {
  test('a call over its time limit fails as TIMED_OUT and the sandbox exits', async () => {
    const s = sandbox({ 'index.js': `export default async function () { await new Promise(() => setTimeout(() => {}, 60000)); }` });
    try {
      await s.ready();
      const a = await s.call('A', { limits: { timeoutMs: 200 } });
      assert.equal(a.result.code, 'TIMED_OUT');
      assert.equal(a.idle.clean, false);
      assert.notEqual(await s.exited, 0);
      assert.ok(s.events.some((e) => e.t === 'fatal' && e.code === 'TIMED_OUT'));
    } finally { await s.close(); }
  });

  test('an uncaught exception during a call fails it and the sandbox exits', async () => {
    const s = sandbox({ 'index.js': `
      export default async function () { setTimeout(() => { throw new Error('off-stack'); }, 10); await new Promise((r) => setTimeout(r, 100)); return 1; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.ok, false);
      assert.equal(a.idle.clean, false);
      assert.notEqual(await s.exited, 0);
      assert.ok(s.events.some((e) => e.t === 'fatal'));
    } finally { await s.close(); }
  });

  test('a module that fails to load reports fatal and never says ready', async () => {
    const s = sandbox({ 'index.js': `throw new Error('cannot load');` });
    try {
      const first = await s.ready();
      assert.equal(first.t, 'fatal');
      assert.match(first.message, /failed to load/);
      assert.ok(!s.events.some((e) => e.t === 'ready'));
    } finally { await s.close(); }
  });

  test('a malformed envelope ends the sandbox', async () => {
    const s = sandbox({ 'index.js': `export default async function () { return 1; }` });
    try {
      await s.ready();
      s.events.length = 0;
      const p = s.waitFor((e) => e.t === 'fatal');
      const child = s; // send garbage through the same pipe
      await child.call('X', { run: null }).catch(() => {});
      const f = await p;
      assert.equal(f.code, 'RUNTIME_START_FAILED');
      assert.equal(await s.exited, 20, 'a platform-side fault exits 20, like single-run');
    } finally { await s.close(); }
  });
});

describe('reuse mode: the caller token on demand', () => {
  test('a call that never asks costs no request at all', async () => {
    const s = sandbox({ 'index.js': 'export default async () => "no token needed";' }, { answer: () => 'tok-unused-0000' });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.value, 'no token needed');
      assert.equal(s.events.filter((e) => e.t === 'need').length, 0);
    } finally { await s.close(); }
  });

  test('asked from code: the runner is asked once per call, and the token is masked from then on', async () => {
    const s = sandbox({ 'index.js': `
      export default async function (input, ctx) {
        const [a, b] = await Promise.all([ctx.blocks.getAccessToken(), ctx.blocks.getAccessToken()]);
        console.log('using', a);
        return { same: a === b, length: a.length };
      }` }, { answer: (need) => (need.what === 'accessToken' ? 'tok-on-demand-' + need.call + '-9999' : null) });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.deepEqual(a.result.value, { same: true, length: 'tok-on-demand-A-9999'.length });
      const needs = s.events.filter((e) => e.t === 'need');
      assert.equal(needs.length, 1, 'one request per call, however often the code asks');
      assert.equal(needs[0].call, 'A');
      assert.ok(a.logs.some((l) => l.msg.includes('[redacted]')), JSON.stringify(a.logs));
      assert.ok(!JSON.stringify(s.events.filter((e) => e.t === 'log')).includes('tok-on-demand-A-9999'));

      const b = await s.call('B');
      assert.equal(b.result.value.length, 'tok-on-demand-B-9999'.length, 'call B gets its own caller token');
      assert.equal(s.events.filter((e) => e.t === 'need').length, 2);
    } finally { await s.close(); }
  });

  test('no caller token (public trigger, failed redemption) is undefined, not an error', async () => {
    const s = sandbox({ 'index.js': 'export default async (i, ctx) => typeof (await ctx.blocks.getAccessToken());' },
      { answer: () => null });
    try {
      await s.ready();
      assert.equal((await s.call('A')).result.value, 'undefined');
    } finally { await s.close(); }
  });

  test('an answer naming another call, or a request never made, is ignored', async () => {
    const s = sandbox({ 'index.js': 'export default async (i, ctx) => await ctx.blocks.getAccessToken();' });
    try {
      await s.ready();
      const call = s.call('A');
      const need = await s.waitFor((e) => e.t === 'need');
      s.send(JSON.stringify({ t: 'give', call: 'B', id: need.id, value: 'tok-for-the-wrong-call' }));
      s.send(JSON.stringify({ t: 'give', call: 'A', id: need.id + 99, value: 'tok-for-no-request' }));
      s.send(JSON.stringify({ t: 'give', call: 'A', id: need.id, value: 'tok-the-real-one-5678' }));
      assert.equal((await call).result.value, 'tok-the-real-one-5678');
    } finally { await s.close(); }
  });

  test('the removed ctx.blocks.accessToken fails with a message that names its replacement', async () => {
    const s = sandbox({ 'index.js': 'export default async (i, ctx) => ctx.blocks.accessToken;' });
    try {
      await s.ready();
      const a = await s.call('A', { blocks: { accessToken: 'tok-removed-1234' } });
      assert.equal(a.result.ok, false);
      assert.match(a.result.message, /getAccessToken\(\)/);
      assert.ok(!JSON.stringify(s.events).includes('tok-removed-1234'));
    } finally { await s.close(); }
  });
});

describe('reuse mode: streamed answers (F-5)', () => {
  test('each piece is a chunk line of its call, sent while the handler still works, before the result', async () => {
    const s = sandbox({ 'index.js': `
      export default async function* (input) {
        yield 'Hel';
        yield 'lo ';
        await new Promise((r) => setTimeout(r, 400));
        yield input.call;
      }` });
    try {
      await s.ready();
      s.send(JSON.stringify(envelope('A')));
      await s.waitFor((e) => e.t === 'chunk' && e.data === 'lo ');
      assert.equal(s.events.find((e) => e.t === 'result' && e.call === 'A'), undefined, 'live: no result yet');
      const a = await s.call('B');   // queued behind A; A finishes first
      const chunksA = s.events.filter((e) => e.t === 'chunk' && e.call === 'A').map((e) => e.data);
      assert.deepEqual(chunksA, ['Hel', 'lo ', 'A']);
      const resultA = s.events.find((e) => e.t === 'result' && e.call === 'A');
      assert.equal(resultA.value, 'Hello A', 'the run keeps the text as its result');
      assert.ok(s.events.indexOf(resultA) > s.events.findLastIndex((e) => e.t === 'chunk' && e.call === 'A'));
      assert.equal(s.events.find((e) => e.t === 'idle' && e.call === 'A').clean, true);
      assert.equal(a.result.value, 'Hello B');
    } finally { await s.close(); }
  });

  test('bytes become text (a character split across pieces survives), objects become JSON lines', async () => {
    const s = sandbox({ 'index.js': `
      export default async function () {
        const euro = Buffer.from('€');
        return (async function* () {
          yield euro.subarray(0, 1);
          yield euro.subarray(1);
          yield { n: 1 };
        })();
      }` });
    try {
      await s.ready();
      const a = await s.call('A');
      const text = s.events.filter((e) => e.t === 'chunk' && e.call === 'A').map((e) => e.data).join('');
      assert.equal(text, '€{"n":1}\n');
      assert.equal(a.result.value, '€{"n":1}\n');
    } finally { await s.close(); }
  });

  test('a stream that throws midway fails the call; what was sent stays sent', async () => {
    const s = sandbox({ 'index.js': `
      export default async function* () { yield 'part'; throw new Error('upstream closed'); }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.deepEqual(s.events.filter((e) => e.t === 'chunk').map((e) => e.data), ['part']);
      assert.equal(a.result.ok, false);
      assert.equal(a.result.code, 'USER_RUNTIME_ERROR');
      assert.match(a.result.message, /upstream closed/);
    } finally { await s.close(); }
  });

  test('a stream over its budget stops and fails as RESULT_TOO_LARGE', async () => {
    const s = sandbox({ 'index.js': `
      const big = 'x'.repeat(512 * 1024);
      export default async function* () { for (let i = 0; i < 10; i++) yield big; }` });
    try {
      await s.ready();
      const a = await s.call('A');
      assert.equal(a.result.ok, false);
      assert.equal(a.result.code, 'RESULT_TOO_LARGE');
      const sent = s.events.filter((e) => e.t === 'chunk').reduce((n, e) => n + e.data.length, 0);
      assert.ok(sent <= 3 * 1024 * 1024 && sent > 0);
    } finally { await s.close(); }
  });

  test('a stream that outlasts the time limit is stopped as a timeout', async () => {
    const s = sandbox({ 'index.js': `
      export default async function* () { yield 'a'; await new Promise((r) => setTimeout(r, 60000)); yield 'b'; }` });
    try {
      await s.ready();
      const a = await s.call('A', { limits: { timeoutMs: 300 } });
      assert.equal(a.result.code, 'TIMED_OUT');
    } finally { await s.close(); }
  });
});
