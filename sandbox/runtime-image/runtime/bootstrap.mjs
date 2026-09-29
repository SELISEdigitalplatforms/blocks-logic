// bootstrap.mjs — the trusted entrypoint of every function container.
//
// It is PID 1 inside a gVisor sandbox that has no capabilities, no writable root and no
// credentials. Its job is narrow and auditable:
//
//   1. read and validate the execution envelope the runner bind-mounted read-only;
//   2. build a frozen `ctx` from it and redirect console.* into the stdout protocol;
//   3. import /function/index.js and await its default export;
//   4. emit exactly one `result` line and exit 0, 10 or 20.
//
// Everything it needs is captured before tenant code is imported (see protocol.mjs). It
// hardens, it does not isolate: isolation is gVisor, the cgroup ceilings and the runner's
// out-of-process caps. The runner owns the hard kill; the soft deadline here only exists so
// an over-running function reports a clean TIMED_OUT instead of dying under SIGKILL with no
// output at all.

import { readFileSync } from 'node:fs';
import { ProtocolWriter, LIMITS, EXIT, CODE } from './protocol.mjs';
import { parseEnvelope, EnvelopeError } from './envelope.mjs';

const ENVELOPE_PATH = process.env.BLOCKS_EXECUTION_FILE || '/run/blocks/execution.json';
const FUNCTION_ENTRY = process.env.BLOCKS_FUNCTION_ENTRY || '/function/index.js';
const RUNTIME_VERSION = process.env.BLOCKS_RUNTIME_VERSION || '1';

const writer = new ProtocolWriter();

// Captured before anything tenant-controlled can run.
const _exit = process.exit.bind(process);
const _setTimeout = setTimeout;
const _clearTimeout = clearTimeout;

let finished = false;

/** Emits the final line (once) and leaves. */
function finish(exitCode, failure) {
  if (finished) return;
  finished = true;
  if (failure) writer.failure(failure.code, failure.message, failure.stack);
  // stdout is a pipe to the runner; give Node a tick to flush before exiting.
  process.stdout.write('', () => _exit(exitCode));
  _setTimeout(() => _exit(exitCode), 250).unref?.();
}

function describe(err) {
  if (err instanceof Error) {
    return { message: err.message || String(err), stack: err.stack };
  }
  // Tenant code may throw anything at all, including objects with hostile getters.
  let message;
  try { message = String(err); } catch { message = '<unrepresentable thrown value>'; }
  return { message, stack: undefined };
}

// --------------------------------------------------------------------- ctx ----

function buildContext(envelope) {
  const log = Object.freeze({
    debug: (msg, data) => writer.log('debug', msg, data),
    info:  (msg, data) => writer.log('info', msg, data),
    warn:  (msg, data) => writer.log('warn', msg, data),
    error: (msg, data) => writer.log('error', msg, data),
  });

  return Object.freeze({
    context: envelope.context,
    env: envelope.env,
    run: envelope.run,
    log,
  });
}

/**
 * npm packages log through console, so console must land in the same bounded stream.
 * Formatting mirrors what a developer expects from console.log while staying one JSON
 * object per line.
 */
function patchConsole() {
  const format = (args) => {
    const parts = [];
    for (const a of args) {
      if (typeof a === 'string') { parts.push(a); continue; }
      try { parts.push(inspectish(a)); } catch { parts.push('<unrepresentable>'); }
    }
    return parts.join(' ');
  };

  const inspectish = (v) => {
    if (v === null) return 'null';
    if (v === undefined) return 'undefined';
    if (typeof v === 'bigint') return `${v}n`;
    if (typeof v === 'symbol') return v.toString();
    if (typeof v === 'function') return `[Function: ${v.name || 'anonymous'}]`;
    if (v instanceof Error) return v.stack || `${v.name}: ${v.message}`;
    if (typeof v === 'object') {
      try { return JSON.stringify(v); } catch { return '[object]'; }
    }
    return String(v);
  };

  const bind = (level) => (...args) => writer.log(level, format(args));

  const patched = {
    log: bind('info'),
    info: bind('info'),
    debug: bind('debug'),
    trace: bind('debug'),
    warn: bind('warn'),
    error: bind('error'),
    dir: bind('info'),
    table: bind('info'),
    // Deliberately inert: they write nothing useful down a line protocol.
    group: () => {}, groupEnd: () => {}, groupCollapsed: () => {},
    time: () => {}, timeEnd: () => {}, timeLog: () => {},
    count: () => {}, countReset: () => {}, assert: () => {},
  };
  for (const [k, v] of Object.entries(patched)) console[k] = v;
}

// ------------------------------------------------------------------- main ----

async function main() {
  // --- envelope ---------------------------------------------------------------
  let envelope;
  try {
    const raw = readFileSync(ENVELOPE_PATH, 'utf8');
    if (Buffer.byteLength(raw, 'utf8') > 1024 * 1024) {
      throw new EnvelopeError('envelope exceeds the 1 MB input ceiling');
    }
    envelope = parseEnvelope(raw);
  } catch (err) {
    const d = describe(err);
    return finish(EXIT.BOOTSTRAP_ERROR, {
      code: CODE.RUNTIME_START_FAILED,
      message: `cannot read the execution envelope at ${ENVELOPE_PATH}: ${d.message}`,
      stack: d.stack,
    });
  }

  // Armed before the function is imported, and before ctx exists: from here on every line the
  // writer emits — ctx.log, console, an uncaught error's message and stack — has each
  // secret-backed value masked out. Top-level code in the tenant's module runs during that
  // import, so arming any later would leave exactly one window open.
  writer.useRedaction(envelope.maskedValues);

  const ctx = buildContext(envelope);
  process.title = `blocks-fn ${envelope.run.id}`;
  patchConsole();

  // A function that leaves a rejected promise or throws off-stack still gets one clean
  // result line rather than Node's default crash output.
  process.on('unhandledRejection', (reason) => {
    const d = describe(reason);
    finish(EXIT.USER_ERROR, { code: CODE.USER_RUNTIME_ERROR, message: `unhandled rejection: ${d.message}`, stack: d.stack });
  });
  process.on('uncaughtException', (err) => {
    const d = describe(err);
    finish(EXIT.USER_ERROR, { code: CODE.USER_RUNTIME_ERROR, message: d.message, stack: d.stack });
  });
  // The runner sends SIGTERM before its hard kill. Exit without a result line: the runner
  // knows whether it was cancelling or timing out and maps the status accordingly.
  process.on('SIGTERM', () => { finished = true; _exit(143); });

  // --- load the function ------------------------------------------------------
  let handler;
  try {
    const mod = await import(FUNCTION_ENTRY);
    handler = mod?.default;
  } catch (err) {
    const d = describe(err);
    return finish(EXIT.USER_ERROR, {
      code: CODE.USER_RUNTIME_ERROR,
      message: `the function module failed to load: ${d.message}`,
      stack: d.stack,
    });
  }
  if (typeof handler !== 'function') {
    return finish(EXIT.USER_ERROR, {
      code: CODE.USER_RUNTIME_ERROR,
      message: 'the function must export a default function: export default async function (input, ctx) { … }',
    });
  }

  // --- run it -----------------------------------------------------------------
  const timeoutMs = envelope.limits.timeoutMs;
  let timer = null;
  const deadline = timeoutMs
    ? new Promise((_, reject) => {
        timer = _setTimeout(() => reject(Object.assign(new Error(
          `the function exceeded its ${timeoutMs} ms time limit`), { __timeout: true })), timeoutMs);
        timer.unref?.();
      })
    : null;

  let value;
  try {
    value = deadline
      ? await Promise.race([handler(envelope.input, ctx), deadline])
      : await handler(envelope.input, ctx);
  } catch (err) {
    if (timer) _clearTimeout(timer);
    const d = describe(err);
    return finish(EXIT.USER_ERROR, {
      code: err?.__timeout ? CODE.TIMED_OUT : CODE.USER_RUNTIME_ERROR,
      message: d.message,
      stack: err?.__timeout ? undefined : d.stack,
    });
  }
  if (timer) _clearTimeout(timer);

  // --- return the result ------------------------------------------------------
  const problem = writer.result(value);
  if (problem === CODE.RESULT_TOO_LARGE) {
    return finish(EXIT.USER_ERROR, {
      code: CODE.RESULT_TOO_LARGE,
      message: `the result exceeds the ${LIMITS.RESULT_BYTES} byte ceiling`,
    });
  }
  if (problem === CODE.RESULT_NOT_SERIALIZABLE) {
    return finish(EXIT.USER_ERROR, {
      code: CODE.RESULT_NOT_SERIALIZABLE,
      message: 'the returned value cannot be serialized to JSON (circular reference, BigInt or a throwing toJSON)',
    });
  }
  return finish(EXIT.OK);
}

// A failure in the bootstrap itself is a platform fault, never the tenant's.
main().catch((err) => {
  const d = describe(err);
  finish(EXIT.BOOTSTRAP_ERROR, {
    code: CODE.RUNTIME_START_FAILED,
    message: `runtime ${RUNTIME_VERSION} failed: ${d.message}`,
    stack: d.stack,
  });
});
