// bootstrap.test.mjs — node:test suite for the trusted runtime.
//
//   node --test runtime/                 (on the host, Node 22+)
//   docker run --rm --entrypoint node blocks-functions-node:24-v1 --test /runtime/
//
// The end-to-end cases spawn the real bootstrap against a temporary envelope and function,
// so what is asserted is the actual stdout protocol, not a mock of it.

import { test, describe } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { createServer } from 'node:http';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

import { ProtocolWriter, LIMITS, EXIT, CODE } from './protocol.mjs';
import { parseEnvelope, EnvelopeError } from './envelope.mjs';
import { withTraceparent } from './trace.mjs';

const HERE = dirname(fileURLToPath(import.meta.url));
const BOOTSTRAP = join(HERE, 'bootstrap.mjs');

// ---------------------------------------------------------------- helpers ----

/** A writer that collects lines instead of writing to stdout. */
function collector() {
  const lines = [];
  const w = new ProtocolWriter((s) => lines.push(s));
  return { w, lines, parsed: () => lines.map((l) => JSON.parse(l)) };
}

const BASE_ENVELOPE = {
  run: { id: 'run_test_1', functionId: 'fn_1', version: 3, attempt: 1,
         invokedBy: { type: 'http', id: null } },
  context: { tenantId: 't1', userId: 'u1', organizationId: 'o1', roles: ['Admin'],
             permissions: ['orders.read'], email: 'u@example.com',
             isAuthenticated: true, impersonated: false, applicationDomain: 'app.example.com' },
  env: { PUBLIC_API: 'https://example.invalid', REGION: 'ch' },
  input: { orderId: '123' },
  limits: { timeoutMs: 5000 },
};

/** Runs the real bootstrap over a temporary function + envelope. */
function runBootstrap(source, envelope = BASE_ENVELOPE, { timeoutMs = 20000 } = {}) {
  const dir = mkdtempSync(join(tmpdir(), 'fnboot-'));
  const fnPath = join(dir, 'index.js');
  const envPath = join(dir, 'execution.json');
  writeFileSync(fnPath, source, 'utf8');
  writeFileSync(envPath, JSON.stringify(envelope), 'utf8');

  return new Promise((resolve) => {
    const child = spawn(process.execPath, [BOOTSTRAP], {
      env: { ...process.env, BLOCKS_EXECUTION_FILE: envPath, BLOCKS_FUNCTION_ENTRY: fnPath },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let out = '', err = '';
    const killer = setTimeout(() => child.kill('SIGKILL'), timeoutMs);
    child.stdout.on('data', (d) => { out += d; });
    child.stderr.on('data', (d) => { err += d; });
    child.on('close', (code) => {
      clearTimeout(killer);
      rmSync(dir, { recursive: true, force: true });
      const events = out.split('\n').filter(Boolean).map((l) => {
        try { return JSON.parse(l); } catch { return { t: 'unparsed', raw: l }; }
      });
      resolve({ code, events, stderr: err,
                result: events.find((e) => e.t === 'result'),
                logs: events.filter((e) => e.t === 'log'),
                truncated: events.find((e) => e.t === 'truncated') });
    });
  });
}

// --------------------------------------------------------------- envelope ----

describe('envelope validation', () => {
  test('accepts the reference envelope and freezes identity', () => {
    const e = parseEnvelope(JSON.stringify(BASE_ENVELOPE));
    assert.equal(e.run.id, 'run_test_1');
    assert.equal(e.context.tenantId, 't1');
    assert.deepEqual([...e.context.roles], ['Admin']);
    assert.ok(Object.isFrozen(e.context));
    assert.ok(Object.isFrozen(e.env));
    assert.ok(Object.isFrozen(e.run));
  });

  test('tenant code cannot redefine authoritative identity', () => {
    const e = parseEnvelope(JSON.stringify(BASE_ENVELOPE));
    assert.throws(() => { 'use strict'; e.context.tenantId = 'other'; }, TypeError);
    assert.equal(e.context.tenantId, 't1');
  });

  test('a public invocation reports an unauthenticated null user', () => {
    const e = parseEnvelope(JSON.stringify({ run: { id: 'r' }, context: {} }));
    assert.equal(e.context.userId, null);
    assert.equal(e.context.isAuthenticated, false);
    assert.deepEqual([...e.context.roles], []);
  });

  test('rejects malformed JSON, a missing run and a bad run id', () => {
    assert.throws(() => parseEnvelope('{nope'), EnvelopeError);
    assert.throws(() => parseEnvelope('[]'), EnvelopeError);
    assert.throws(() => parseEnvelope('{}'), EnvelopeError);
    assert.throws(() => parseEnvelope('{"run":{"id":""}}'), EnvelopeError);
  });

  test('rejects non-scalar env values rather than passing them through', () => {
    assert.throws(() => parseEnvelope(JSON.stringify({ run: { id: 'r' }, env: { A: { nested: 1 } } })), EnvelopeError);
    const ok = parseEnvelope(JSON.stringify({ run: { id: 'r' }, env: { A: 'x', B: 2, C: true, D: null } }));
    assert.deepEqual({ ...ok.env }, { A: 'x', B: 2, C: true, D: null });
  });

  test('env has a null prototype, so __proto__ cannot be smuggled in', () => {
    const e = parseEnvelope('{"run":{"id":"r"},"env":{"__proto__":"x"}}');
    assert.equal(Object.getPrototypeOf(e.env), null);
    assert.equal({}.x, undefined);
  });

  test('rejects a non-positive timeout', () => {
    assert.throws(() => parseEnvelope('{"run":{"id":"r"},"limits":{"timeoutMs":0}}'), EnvelopeError);
    assert.throws(() => parseEnvelope('{"run":{"id":"r"},"limits":{"timeoutMs":"soon"}}'), EnvelopeError);
  });

  test('blocks.accessToken is undefined when the runner added none', () => {
    for (const blocks of [undefined, null, {}, { accessToken: null }, { accessToken: '' }]) {
      const e = parseEnvelope(JSON.stringify({ run: { id: 'r' }, blocks }));
      assert.equal(e.blocks.accessToken, undefined, JSON.stringify(blocks));
      assert.ok(Object.isFrozen(e.blocks));
      assert.deepEqual([...e.maskedValues], []);
    }
  });

  test('a delegated access token is exposed, frozen and always masked', () => {
    const e = parseEnvelope(JSON.stringify({ run: { id: 'r' }, blocks: { accessToken: 'eyJ.delegated.token' } }));
    assert.equal(e.blocks.accessToken, 'eyJ.delegated.token');
    assert.throws(() => { 'use strict'; e.blocks.accessToken = 'other'; }, TypeError);
    assert.deepEqual([...e.maskedValues], ['eyJ.delegated.token']);
  });

  test('a malformed blocks section is refused, not ignored', () => {
    for (const blocks of ['eyJ.token', ['eyJ.token'], { accessToken: 42 }, { accessToken: { t: 1 } }]) {
      assert.throws(
        () => parseEnvelope(JSON.stringify({ run: { id: 'r' }, blocks })), EnvelopeError, JSON.stringify(blocks));
    }
  });
});

// --------------------------------------------------------------- protocol ----

describe('protocol writer', () => {
  test('writes well-formed log lines', () => {
    const { w, parsed } = collector();
    w.log('info', 'hello', { a: 1 });
    const [e] = parsed();
    assert.equal(e.t, 'log');
    assert.equal(e.level, 'info');
    assert.equal(e.msg, 'hello');
    assert.deepEqual(e.data, { a: 1 });
    assert.match(e.ts, /^\d{4}-\d{2}-\d{2}T/);
  });

  test('an unknown level falls back to info', () => {
    const { w, parsed } = collector();
    w.log('fatal', 'x');
    assert.equal(parsed()[0].level, 'info');
  });

  test('stops at 10 000 lines and emits one truncation event', () => {
    const { w, parsed } = collector();
    for (let i = 0; i < LIMITS.LOG_LINES + 50; i++) w.log('info', 'x');
    const events = parsed();
    assert.equal(events.filter((e) => e.t === 'log').length, LIMITS.LOG_LINES);
    const truncs = events.filter((e) => e.t === 'truncated');
    assert.equal(truncs.length, 1);
    assert.equal(truncs[0].reason, 'log_lines');
  });

  test('stops at 1 MB and emits one truncation event', () => {
    const { w, parsed } = collector();
    const big = 'y'.repeat(4096);
    for (let i = 0; i < 400; i++) w.log('info', big);
    const events = parsed();
    const truncs = events.filter((e) => e.t === 'truncated');
    assert.equal(truncs.length, 1);
    assert.equal(truncs[0].reason, 'log_bytes');
    assert.ok(w.byteCount <= LIMITS.LOG_BYTES);
  });

  test('a single oversized message is clipped, not dropped', () => {
    const { w, parsed } = collector();
    w.log('info', 'z'.repeat(LIMITS.MESSAGE_CHARS * 3));
    const [e] = parsed();
    assert.ok(e.msg.length < LIMITS.MESSAGE_CHARS + 32);
    assert.ok(e.msg.endsWith('[clipped]'));
  });

  test('unserializable log data does not lose the message', () => {
    const { w, parsed } = collector();
    const circular = {}; circular.self = circular;
    w.log('info', 'still here', circular);
    const [e] = parsed();
    assert.equal(e.msg, 'still here');
    assert.deepEqual(e.data, { _unserializable: true });
  });

  test('the result line is written once and only once', () => {
    const { w, parsed } = collector();
    assert.equal(w.result({ a: 1 }), null);
    assert.equal(w.result({ b: 2 }), null);
    const results = parsed().filter((e) => e.t === 'result');
    assert.equal(results.length, 1);
    assert.deepEqual(results[0].value, { a: 1 });
  });

  test('a failure is still written after the log budget is exhausted', () => {
    const { w, parsed } = collector();
    for (let i = 0; i < LIMITS.LOG_LINES + 10; i++) w.log('info', 'x');
    w.failure(CODE.USER_RUNTIME_ERROR, 'boom', 'stack here');
    const result = parsed().find((e) => e.t === 'result');
    assert.equal(result.ok, false);
    assert.equal(result.code, CODE.USER_RUNTIME_ERROR);
    assert.equal(result.message, 'boom');
  });

  test('reports oversized and unserializable results', () => {
    const big = collector();
    assert.equal(big.w.result({ blob: 'x'.repeat(LIMITS.RESULT_BYTES + 1024) }), CODE.RESULT_TOO_LARGE);

    const circular = collector();
    const c = {}; c.self = c;
    assert.equal(circular.w.result(c), CODE.RESULT_NOT_SERIALIZABLE);

    const bigint = collector();
    assert.equal(bigint.w.result({ n: 1n }), CODE.RESULT_NOT_SERIALIZABLE);
  });

  test('undefined becomes an explicit null result', () => {
    const { w, parsed } = collector();
    w.result(undefined);
    assert.equal(parsed()[0].value, null);
  });
});

// -------------------------------------------------------------- end to end ----

describe('bootstrap end to end', () => {
  test('returns the handler value and exits 0', async () => {
    const r = await runBootstrap('export default async (input) => ({ echoed: input.orderId });');
    assert.equal(r.code, EXIT.OK);
    assert.equal(r.result.ok, true);
    assert.deepEqual(r.result.value, { echoed: '123' });
  });

  test('runs an envelope under the 2 MB input ceiling', async () => {
    const blob = 'x'.repeat(1.5 * 1024 * 1024);
    const r = await runBootstrap('export default async (input) => ({ size: input.blob.length });',
      { ...BASE_ENVELOPE, input: { blob } });
    assert.equal(r.code, EXIT.OK);
    assert.equal(r.result.ok, true);
    assert.deepEqual(r.result.value, { size: blob.length });
  });

  test('refuses an envelope over the 2 MB input ceiling before the function runs', async () => {
    const blob = 'x'.repeat(2 * 1024 * 1024 + 64 * 1024);
    const r = await runBootstrap('export default async () => ({ ran: true });',
      { ...BASE_ENVELOPE, input: { blob } });
    assert.equal(r.code, EXIT.BOOTSTRAP_ERROR);
    assert.equal(r.result.ok, false);
    assert.equal(r.result.code, CODE.RUNTIME_START_FAILED);
    assert.match(r.result.message ?? r.result.error?.message ?? '', /2 MB input ceiling/);
  });

  test('exposes a frozen ctx built from the envelope', async () => {
    const r = await runBootstrap(`export default async (input, ctx) => ({
      tenantId: ctx.context.tenantId,
      authed: ctx.context.isAuthenticated,
      region: ctx.env.REGION,
      runId: ctx.run.id,
      attempt: ctx.run.attempt,
      invokedBy: ctx.run.invokedBy.type,
      frozen: Object.isFrozen(ctx) && Object.isFrozen(ctx.context) && Object.isFrozen(ctx.env),
    });`);
    assert.deepEqual(r.result.value, {
      tenantId: 't1', authed: true, region: 'ch', runId: 'run_test_1',
      attempt: 1, invokedBy: 'http', frozen: true,
    });
  });

  test('an event run: ctx.run.invokedBy carries type, id and the event name, frozen', async () => {
    const envelope = {
      ...BASE_ENVELOPE,
      run: { ...BASE_ENVELOPE.run, invokedBy: { type: 'event', id: 'run-9-1-1', name: 'order.created' } },
      input: { orderId: '7' },
    };
    const r = await runBootstrap(`export default async (i, ctx) => {
      let blocked = false;
      try { ctx.run.invokedBy.name = 'x'; } catch { blocked = true; }
      return { by: ctx.run.invokedBy, input: i, blocked, frozen: Object.isFrozen(ctx.run.invokedBy) };
    };`, envelope);
    assert.deepEqual(r.result.value, {
      by: { type: 'event', id: 'run-9-1-1', name: 'order.created' },
      input: { orderId: '7' }, blocked: true, frozen: true,
    });
  });

  test('a run that no event started has invokedBy.name null', async () => {
    const r = await runBootstrap(`export default async (i, ctx) => ({ name: ctx.run.invokedBy.name });`);
    assert.equal(r.result.value.name, null);
  });

  test('mutating ctx.context throws inside the function', async () => {
    const r = await runBootstrap(`export default async (i, ctx) => {
      try { ctx.context.tenantId = 'evil'; } catch (e) { return { blocked: true, still: ctx.context.tenantId }; }
      return { blocked: false, still: ctx.context.tenantId };
    };`);
    assert.equal(r.result.value.still, 't1');
  });

  test('ctx.log and console both reach the protocol', async () => {
    const r = await runBootstrap(`export default async (i, ctx) => {
      ctx.log.info('from ctx', { k: 1 });
      console.log('from console', 42);
      console.error('bad thing');
      console.warn('careful');
      return 'done';
    };`);
    assert.equal(r.result.value, 'done');
    const msgs = r.logs.map((l) => `${l.level}:${l.msg}`);
    assert.ok(msgs.includes('info:from ctx'));
    assert.ok(msgs.includes('info:from console 42'));
    assert.ok(msgs.includes('error:bad thing'));
    assert.ok(msgs.includes('warn:careful'));
  });

  test('a thrown error becomes USER_RUNTIME_ERROR with exit 10', async () => {
    const r = await runBootstrap("export default async () => { throw new Error('kaboom'); };");
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.ok, false);
    assert.equal(r.result.code, CODE.USER_RUNTIME_ERROR);
    assert.equal(r.result.message, 'kaboom');
    assert.match(r.result.stack, /kaboom/);
  });

  test('an error names its type and code, and the stack carries its details', async () => {
    const r = await runBootstrap("export default async () => { new URL('not a url'); };");
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.message, 'TypeError [ERR_INVALID_URL]: Invalid URL');
    assert.match(r.result.stack, /code: "?ERR_INVALID_URL/);
    assert.match(r.result.stack, /input: not a url/);
  });

  test('the whole cause chain is reported, not just the outer error', async () => {
    const r = await runBootstrap(`export default async () => {
      const low = Object.assign(new Error('connect ECONNREFUSED 1.2.3.4:6380'), { code: 'ECONNREFUSED', port: 6380 });
      throw new Error('could not reach the cache', { cause: low });
    };`);
    assert.equal(r.result.message, 'could not reach the cache');
    assert.match(r.result.stack, /Caused by: Error: connect ECONNREFUSED/);
    assert.match(r.result.stack, /port: 6380/);
  });

  test('an AggregateError lists what it aggregates', async () => {
    const r = await runBootstrap(`export default async () => {
      throw new AggregateError([new Error('first'), new TypeError('second')], 'all failed');
    };`);
    assert.equal(r.result.message, 'AggregateError: all failed');
    assert.match(r.result.stack, /errors \(2\):/);
    assert.match(r.result.stack, /- TypeError: second/);
  });

  test('an error with hostile getters is still reported', async () => {
    const r = await runBootstrap(`export default async () => {
      const e = new Error('odd');
      Object.defineProperty(e, 'code', { get() { throw new Error('nope'); } });
      Object.defineProperty(e, 'cause', { get() { throw new Error('nope'); } });
      throw e;
    };`);
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.message, 'odd');
  });

  test('a non-Error throw is still reported cleanly', async () => {
    const r = await runBootstrap('export default async () => { throw "just a string"; };');
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.message, 'just a string');
  });

  test('a missing default export is a user error, not a crash', async () => {
    const r = await runBootstrap('export const notDefault = 1;');
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.match(r.result.message, /must export a default function/);
  });

  test('a module that throws while loading is a user error', async () => {
    const r = await runBootstrap("throw new Error('bad import');\nexport default async () => 1;");
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.match(r.result.message, /failed to load/);
  });

  test('a missing envelope is a bootstrap error with exit 20', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'fnboot-'));
    const fnPath = join(dir, 'index.js');
    writeFileSync(fnPath, 'export default async () => 1;');
    const r = await new Promise((resolve) => {
      const child = spawn(process.execPath, [BOOTSTRAP], {
        env: { ...process.env, BLOCKS_EXECUTION_FILE: join(dir, 'nope.json'), BLOCKS_FUNCTION_ENTRY: fnPath },
        stdio: ['ignore', 'pipe', 'pipe'],
      });
      let out = '';
      child.stdout.on('data', (d) => { out += d; });
      child.on('close', (code) => resolve({ code, out }));
    });
    rmSync(dir, { recursive: true, force: true });
    assert.equal(r.code, EXIT.BOOTSTRAP_ERROR);
    const ev = JSON.parse(r.out.trim());
    assert.equal(ev.code, CODE.RUNTIME_START_FAILED);
  });

  test('the soft deadline reports TIMED_OUT instead of hanging', async () => {
    const r = await runBootstrap(
      'export default async () => new Promise((r) => setTimeout(r, 60000));',
      { ...BASE_ENVELOPE, limits: { timeoutMs: 600 } });
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.code, CODE.TIMED_OUT);
    assert.match(r.result.message, /600 ms/);
  });

  test('an oversized result is rejected, not streamed', async () => {
    const r = await runBootstrap(
      "export default async () => ({ blob: 'x'.repeat(6 * 1024 * 1024) });");
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.result.code, CODE.RESULT_TOO_LARGE);
  });

  test('a circular result is rejected', async () => {
    const r = await runBootstrap('export default async () => { const o = {}; o.self = o; return o; };');
    assert.equal(r.result.code, CODE.RESULT_NOT_SERIALIZABLE);
  });

  test('a BigInt result is rejected', async () => {
    const r = await runBootstrap('export default async () => ({ n: 10n });');
    assert.equal(r.result.code, CODE.RESULT_NOT_SERIALIZABLE);
  });

  test('an unhandled rejection produces one result line', async () => {
    const r = await runBootstrap(`export default async () => {
      Promise.reject(new Error('floating'));
      return new Promise((res) => setTimeout(() => res('never'), 5000));
    };`);
    assert.equal(r.code, EXIT.USER_ERROR);
    assert.equal(r.events.filter((e) => e.t === 'result').length, 1);
    assert.match(r.result.message, /floating/);
  });

  test('log flooding is truncated once and the result still arrives', async () => {
    const r = await runBootstrap(`export default async (i, ctx) => {
      for (let n = 0; n < 20000; n++) ctx.log.info('flood ' + n);
      return 'survived';
    };`);
    assert.equal(r.code, EXIT.OK);
    assert.equal(r.result.value, 'survived');
    assert.ok(r.truncated, 'expected a truncated event');
    assert.ok(r.logs.length <= LIMITS.LOG_LINES);
  });

  test('a function that replaces JSON.stringify cannot corrupt the result line', async () => {
    const r = await runBootstrap(`export default async () => {
      JSON.stringify = () => '{"t":"result","ok":true,"value":"hijacked"}';
      return { honest: true };
    };`);
    assert.equal(r.result.ok, true);
    assert.deepEqual(r.result.value, { honest: true });
  });

  test('a function that replaces process.stdout.write cannot suppress the result', async () => {
    const r = await runBootstrap(`export default async () => {
      process.stdout.write = () => true;
      return { delivered: true };
    };`);
    assert.equal(r.code, EXIT.OK);
    assert.deepEqual(r.result.value, { delivered: true });
  });
});

// ---------------------------------------------------------------- redaction ----

describe('secret redaction', () => {
  const lines = (sink) => sink.written.map((l) => JSON.parse(l));
  const newSink = () => {
    const written = [];
    const sink = (line) => written.push(line);
    sink.written = written;
    return sink;
  };

  test('masks a secret-backed value in the message, at any depth of data, and in a stack', () => {
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(['sk_live_abcdef123456']);

    writer.log('error', 'calling with sk_live_abcdef123456');
    writer.log('info', 'nested', { auth: { header: 'Bearer sk_live_abcdef123456' } });
    writer.failure('USER_RUNTIME_ERROR', '401 from https://api?key=sk_live_abcdef123456', 'at f (sk_live_abcdef123456)');

    const out = lines(sink);
    assert.equal(out[0].msg, 'calling with [redacted]');
    assert.equal(out[1].data.auth.header, 'Bearer [redacted]');
    assert.equal(out[2].message, '401 from https://api?key=[redacted]');
    assert.equal(out[2].stack, 'at f ([redacted])');
    assert.ok(!sink.written.join('').includes('sk_live_abcdef123456'));
  });

  test('masks a value that reaches the line JSON-escaped', () => {
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    const secret = 'pa"ss\\word-1234';
    writer.useRedaction([secret]);

    writer.log('info', `value is ${secret}`);

    assert.ok(!sink.written.join('').includes('pa\\"ss'));
    assert.match(lines(sink)[0].msg, /\[redacted\]/);
  });

  test('leaves a plain, non-secret variable alone', () => {
    // Only the keys the envelope marked are masked: redacting every variable would hide the
    // ordinary configuration people log on purpose.
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(['the-secret-value']);

    writer.log('info', 'base is https://api.example.com');

    assert.equal(lines(sink)[0].msg, 'base is https://api.example.com');
  });

  test('refuses to mask a value too short to be one, rather than shredding every line', () => {
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(['ab']);

    writer.log('info', 'a table of absolute values');

    assert.equal(lines(sink)[0].msg, 'a table of absolute values');
  });

  test('masks the longer of two overlapping secrets whole', () => {
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(['tok_123', 'tok_123_extended_tail']);

    writer.log('info', 'used tok_123_extended_tail');

    assert.equal(lines(sink)[0].msg, 'used [redacted]');
  });

  test('charges the log budget for the line it actually writes', () => {
    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(['x'.repeat(200)]);

    writer.log('info', 'x'.repeat(200));

    // The written line is far shorter than the unredacted one; the budget must follow the
    // bytes that left the process, not the bytes that never did.
    assert.ok(writer.byteCount < 200, `byteCount was ${writer.byteCount}`);
    assert.equal(writer.byteCount, Buffer.byteLength(sink.written[0], 'utf8'));
  });

  test('an envelope with no maskedEnv masks nothing and still parses', () => {
    const env = parseEnvelope(JSON.stringify({
      run: { id: 'r1' }, env: { API_BASE: 'https://x' },
    }));
    assert.deepEqual(env.maskedValues, []);
  });

  test('maskedEnv resolves to the values of the keys it names', () => {
    const env = parseEnvelope(JSON.stringify({
      run: { id: 'r1' },
      env: { API_BASE: 'https://x', TOKEN: 'sk_live_9' },
      maskedEnv: ['TOKEN', 'NOT_BOUND'],
    }));
    // A key that names nothing contributes nothing rather than an undefined entry.
    assert.deepEqual(env.maskedValues, ['sk_live_9']);
  });

  test('maskedValues from the runner are added to the maskedEnv values', () => {
    // The runner substitutes an embedded reference ("Bearer {{secret.x}}") and hands over the
    // bare secret as well, so a line that logs only the token is masked too.
    const env = parseEnvelope(JSON.stringify({
      run: { id: 'r1' },
      env: { AUTH: 'Bearer sk_live_embedded_42', PLAIN: 'https://x' },
      maskedEnv: ['AUTH'],
      maskedValues: ['sk_live_embedded_42'],
    }));
    assert.deepEqual(env.maskedValues, ['Bearer sk_live_embedded_42', 'sk_live_embedded_42']);

    const sink = newSink();
    const writer = new ProtocolWriter(sink);
    writer.useRedaction(env.maskedValues);
    writer.log('info', `token=${env.env.AUTH.slice('Bearer '.length)}`);
    writer.log('info', `header=${env.env.AUTH}`);
    const out = lines(sink);
    assert.equal(out[0].msg, 'token=[redacted]');
    assert.equal(out[1].msg, 'header=[redacted]');
    assert.ok(!sink.written.join('').includes('sk_live_embedded_42'));
  });

  test('a maskedValues that is not an array of strings is refused, not ignored', () => {
    // Silently dropping a malformed redaction list would start the function with its secrets
    // unmasked; failing the start is the safe side.
    for (const maskedValues of ['sk_live_x', [42], { a: 'b' }]) {
      assert.throws(
        () => parseEnvelope(JSON.stringify({ run: { id: 'r1' }, env: {}, maskedValues })),
        EnvelopeError);
    }
  });

  test('end to end: a runner-substituted secret is masked in console output, ctx.log and a thrown error', async () => {
    const secret = 'sk_live_end_to_end_77';
    const r = await runBootstrap(`export default async (i, ctx) => {
      console.log('tail', ctx.env.AUTH.split(' ')[1]);
      ctx.log.info('whole ' + ctx.env.AUTH);
      throw new Error('upstream refused ' + ctx.env.AUTH.split(' ')[1]);
    };`, {
      ...BASE_ENVELOPE,
      env: { ...BASE_ENVELOPE.env, AUTH: 'Bearer ' + secret },
      maskedEnv: ['AUTH'],
      maskedValues: [secret],
    });
    const everything = JSON.stringify(r.events) + r.stderr;
    assert.ok(!everything.includes(secret), everything);
    assert.ok(r.logs.length >= 2);
    assert.ok(r.logs.every((l) => l.msg.includes('[redacted]')), JSON.stringify(r.logs));
    assert.equal(r.result.ok, false);
    assert.match(r.result.message, /\[redacted\]/);
  });

  test('end to end: ctx.blocks.getAccessToken() reaches the handler and is masked in logs and errors', async () => {
    // A successful return value is the author's own output and is not scrubbed — the same rule
    // as a secret-bound variable — so this run only logs the token and then fails with it.
    const token = 'eyJhbGciOi.delegated_end_to_end.sig99';
    const r = await runBootstrap(`export default async (i, ctx) => {
      const t = await ctx.blocks.getAccessToken();
      console.log('token is', t);
      ctx.log.info('header', { authorization: 'Bearer ' + t });
      throw new Error('Blocks API refused ' + t);
    };`, { ...BASE_ENVELOPE, blocks: { accessToken: token } });
    const everything = JSON.stringify(r.events) + r.stderr;
    assert.ok(!everything.includes(token), everything);
    assert.equal(r.logs.length, 2);
    assert.ok(r.logs.every((l) => JSON.stringify(l).includes('[redacted]')), JSON.stringify(r.logs));
    assert.equal(r.result.ok, false);
    assert.match(r.result.message, /\[redacted\]/);
  });

  test('end to end: without a delegated token ctx.blocks.getAccessToken() gives undefined', async () => {
    const r = await runBootstrap(
      'export default async (i, ctx) => ({ type: typeof (await ctx.blocks.getAccessToken()), frozen: Object.isFrozen(ctx.blocks) });');
    assert.deepEqual(r.result.value, { type: 'undefined', frozen: true });
  });

  test('the removed ctx.blocks.accessToken fails with a message that names its replacement', async () => {
    const r = await runBootstrap('export default async (i, ctx) => ctx.blocks.accessToken;',
      { ...BASE_ENVELOPE, blocks: { accessToken: 'eyJhbGciOi.removed.sig' } });
    assert.equal(r.result.ok, false);
    assert.match(r.result.message, /getAccessToken\(\)/);
    assert.ok(!JSON.stringify(r.events).includes('eyJhbGciOi.removed.sig'));
  });
});

describe('ctx.waitUntil in single-run mode (tests, workflow steps)', () => {
  test('exists, answers first, and runs the work before the sandbox exits', async () => {
    const r = await runBootstrap(`
      export default async function (input, ctx) {
        ctx.waitUntil(new Promise((ok) => setTimeout(() => { console.log('after the answer'); ok(); }, 50)));
        return 'answered';
      }`);
    assert.equal(r.code, 0);
    assert.equal(r.result.value, 'answered');
    const resultAt = r.events.indexOf(r.result);
    const lateLog = r.events.findIndex((e) => e.t === 'log' && e.msg === 'after the answer');
    assert.ok(lateLog > resultAt, 'the work ran, after the answer was written');
  });

  test('a rejected waitUntil promise is logged, the answer stands', async () => {
    const r = await runBootstrap(`
      export default async function (input, ctx) { ctx.waitUntil(Promise.reject(new Error('audit down'))); return 1; }`);
    assert.equal(r.code, 0);
    assert.equal(r.result.ok, true);
    assert.ok(r.logs.some((l) => /audit down/.test(l.msg)));
  });

  test('waitUntil work past the time limit is stopped, the answer stands', async () => {
    const r = await runBootstrap(`
      export default async function (input, ctx) { ctx.waitUntil(new Promise((r) => setTimeout(r, 60000))); return 1; }`,
      { ...BASE_ENVELOPE, limits: { timeoutMs: 300 } });
    assert.equal(r.code, 0);
    assert.equal(r.result.ok, true);
    assert.ok(r.logs.some((l) => /did not finish within the time limit/.test(l.msg)));
  });
});

describe('streamed answers in single-run mode (F-5)', () => {
  test('the pieces are written and the text is the result', async () => {
    const r = await runBootstrap(`
      export default async function* () { yield 'a'; yield 'b'; yield { c: 1 }; }`);
    assert.equal(r.code, 0);
    assert.deepEqual(r.events.filter((e) => e.t === 'chunk').map((e) => e.data), ['a', 'b', '{"c":1}\n']);
    assert.equal(r.result.value, 'ab{"c":1}\n');
  });

  test('a returned fetch-style body (an async iterable of bytes) streams too', async () => {
    const r = await runBootstrap(`
      export default async function () {
        return new ReadableStream({ start(c) { c.enqueue(new TextEncoder().encode('hi')); c.close(); } });
      }`);
    assert.equal(r.result.value, 'hi');
  });
});

// ------------------------------------------------------------ envelope trace ----

const BLOCKS_URL = 'https://blocks.example.com/';
const TRACEPARENT_A = '00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01';

describe('envelope: trace', () => {
  test('absent → no trace', () => {
    const e = parseEnvelope(JSON.stringify(BASE_ENVELOPE));
    assert.deepEqual(e.trace, { traceparent: null, hosts: [] });
    assert.ok(Object.isFrozen(e.trace));
  });

  test('parses a W3C traceparent and lower-cased hosts', () => {
    const e = parseEnvelope(JSON.stringify({ ...BASE_ENVELOPE,
      trace: { traceparent: TRACEPARENT_A, hosts: ['Api.Example.com', ' '] } }));
    assert.equal(e.trace.traceparent, TRACEPARENT_A);
    assert.deepEqual([...e.trace.hosts], ['api.example.com']);
  });

  test('a malformed traceparent is ignored; wrong shapes are rejected', () => {
    const e = parseEnvelope(JSON.stringify({ ...BASE_ENVELOPE, trace: { traceparent: 'nonsense', hosts: ['a.com'] } }));
    assert.equal(e.trace.traceparent, null);
    assert.throws(() => parseEnvelope(JSON.stringify({ ...BASE_ENVELOPE, trace: { hosts: [1] } })), EnvelopeError);
  });
});

describe('functions cannot send events', () => {
  test('ctx has no events, and an older envelope that still carries an events list runs', async () => {
    const r = await runBootstrap(`
      export default async function (input, ctx) {
        return 'events' in ctx;
      }`, { ...BASE_ENVELOPE, events: { allowed: ['FunctionEvent'] } });
    assert.equal(r.result.value, false);
    assert.equal('events' in r.result, false);
  });
});

// ------------------------------------------------------------ fetch trace ----

describe('withTraceparent (unit)', () => {
  const trace = { traceparent: TRACEPARENT_A, hosts: ['blocks.example.com'] };
  /** Records what the wrapped fetch receives. */
  const recorder = () => {
    const seen = [];
    const base = (input, init) => { seen.push({ input, init }); return Promise.resolve('ok'); };
    return { seen, wrapped: withTraceparent(base, () => trace) };
  };
  const sent = (r) => new Headers(r.seen.at(-1).init?.headers).get('traceparent');

  test('adds the header for the host and its subdomains, case-insensitively', async () => {
    const r = recorder();
    await r.wrapped('https://blocks.example.com/a');
    assert.equal(sent(r), TRACEPARENT_A);
    await r.wrapped('https://API.Blocks.Example.com/a');
    assert.equal(sent(r), TRACEPARENT_A);
  });

  test('leaves other hosts untouched (including look-alikes)', async () => {
    const r = recorder();
    for (const url of ['https://other.com/', 'https://notblocks.example.com/', 'https://blocks.example.com.evil.io/']) {
      await r.wrapped(url);
      assert.equal(r.seen.at(-1).init, undefined);
    }
  });

  test('works with a URL object and a Request, and does not mutate the caller\'s objects', async () => {
    const r = recorder();
    await r.wrapped(new URL('https://blocks.example.com/x'));
    assert.equal(sent(r), TRACEPARENT_A);

    const req = new Request('https://blocks.example.com/y', { headers: { 'x-a': '1' } });
    await r.wrapped(req);
    assert.equal(r.seen.at(-1).input, req);
    assert.equal(sent(r), TRACEPARENT_A);
    assert.equal(new Headers(r.seen.at(-1).init.headers).get('x-a'), '1'); // the Request's own headers kept
    assert.equal(req.headers.has('traceparent'), false);

    const headers = new Headers({ 'x-b': '2' });
    const init = { method: 'POST', headers };
    await r.wrapped('https://blocks.example.com/z', init);
    assert.equal(headers.has('traceparent'), false);
    assert.deepEqual(Object.keys(init), ['method', 'headers']);
    assert.equal(r.seen.at(-1).init.method, 'POST');
  });

  test('init headers as a plain object or array of pairs work', async () => {
    const r = recorder();
    await r.wrapped(BLOCKS_URL, { headers: { 'x-a': '1' } });
    assert.equal(sent(r), TRACEPARENT_A);
    await r.wrapped(BLOCKS_URL, { headers: [['x-a', '1']] });
    assert.equal(sent(r), TRACEPARENT_A);
  });

  test('never overrides a traceparent the caller set', async () => {
    const r = recorder();
    const mine = '00-11111111111111111111111111111111-2222222222222222-01';
    await r.wrapped(BLOCKS_URL, { headers: { Traceparent: mine } });
    assert.equal(sent(r), mine);
    await r.wrapped(new Request(BLOCKS_URL, { headers: { traceparent: mine } }));
    assert.equal(r.seen.at(-1).init, undefined);
  });

  test('an invalid URL, no trace or no hosts pass through unchanged', async () => {
    const r = recorder();
    await r.wrapped('/relative');
    assert.equal(r.seen.at(-1).init, undefined);
    for (const t of [null, { traceparent: null, hosts: ['blocks.example.com'] }, { traceparent: TRACEPARENT_A, hosts: [] }]) {
      const w = withTraceparent((i, n) => { r.seen.push({ input: i, init: n }); return Promise.resolve(); }, () => t);
      await w(BLOCKS_URL);
      assert.equal(r.seen.at(-1).init, undefined);
    }
  });
});

describe('fetch trace in single-run mode', () => {
  test('the header reaches a listed host and not an unlisted one', async () => {
    const seen = [];
    const server = createServer((req, res) => { seen.push(req.headers.traceparent ?? null); res.end('ok'); });
    await new Promise((r) => server.listen(0, '127.0.0.1', r));
    const port = server.address().port;
    try {
      const r = await runBootstrap(`
        export default async function () {
          await fetch('http://127.0.0.1:${port}/listed');
          await fetch('http://localhost:${port}/unlisted');
          return 1;
        }`, { ...BASE_ENVELOPE, trace: { traceparent: TRACEPARENT_A, hosts: ['127.0.0.1'] } });
      assert.equal(r.result.ok, true);
      assert.deepEqual(seen, [TRACEPARENT_A, null]);
    } finally { server.close(); }
  });
});
