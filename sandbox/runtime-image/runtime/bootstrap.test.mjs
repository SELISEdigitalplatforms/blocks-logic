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
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

import { ProtocolWriter, LIMITS, EXIT, CODE } from './protocol.mjs';
import { parseEnvelope, EnvelopeError } from './envelope.mjs';

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
});

