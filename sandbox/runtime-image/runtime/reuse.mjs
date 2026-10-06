// reuse.mjs — reuse mode: one sandbox serves many calls of ONE function version, one at a time.
//
// Trusted code, loaded by bootstrap.mjs only when BLOCKS_RUNTIME_MODE=reuse, before any tenant code
// is imported, so every primitive it relies on is captured here first (see protocol.mjs).
//
// Wire protocol (runner <-> sandbox), one JSON object per line:
//   stdin   one execution envelope per call — exactly the JSON single-run mode reads from
//           /run/blocks/execution.json. Calls are handled strictly one after another.
//   stdout  {t:"ready"}                                    module imported, waiting for calls
//           {t:"started", call} / {t:"log", call} / {t:"result", call}   as in single-run mode
//           {t:"idle", call, clean, leftovers[], late, rssBytes}          call fully over
//           {t:"fatal", code, message}                     the sandbox cannot go on; it exits
//   The runner may hand the next call only after `idle` with clean:true, and must destroy the
//   sandbox after clean:false, a `fatal` line, an exit, or its own hard deadline.
//
// What "clean" means. The customer's code cannot be controlled, so the one thing this file must
// guarantee is that work started by one call does not keep running inside the next caller's call.
// Every async resource created while a call runs is tagged with that call (AsyncLocalStorage +
// async_hooks). After the answer, the call is clean only when none of these is still alive:
//   - a timer or immediate created by the function's OWN code (not node_modules, not Node itself),
//     whether or not it was unref()'d — an unref'd timer still fires in a process that stays up;
//   - an HTTP client request, a fetch() still in flight, a file operation, a DNS lookup or a
//     connect in progress, a child process or a worker thread — whatever started them.
// Long-lived library state is allowed on purpose: a MongoDB or Redis client's sockets and
// heartbeat timers, an HTTP keep-alive pool. Keeping exactly those is what reuse is for.
// Measured with the real drivers: mongodb's first (connecting) call leaves ref'd heartbeat
// timers and sockets, later calls leave nothing; ioredis and an awaited fetch leave nothing
// that counts. Code bundled into index.js (libraries included) is all "own code" to this check.
//
// This catches mistakes, not a determined tenant: the tenant's code shares this process. Its
// reach is bounded by the runner (pause between calls, recycle, hard deadline) and by the fact
// that a function's author already sees every one of its callers' inputs.

import { AsyncLocalStorage, createHook } from 'node:async_hooks';
import { createInterface } from 'node:readline';
import { dirname } from 'node:path';
import { getCallSites } from 'node:util';
import { ProtocolWriter, CODE, EXIT } from './protocol.mjs';
import { parseEnvelope } from './envelope.mjs';

// Captured before any tenant code is imported (see protocol.mjs): a function that later replaces
// these cannot change what the runtime does with them.
const _now = Date.now;
const _stdoutWrite = process.stdout.write.bind(process.stdout);
const _exit = process.exit.bind(process);
const _setTimeout = setTimeout;
const _clearTimeout = clearTimeout;
const _setImmediate = setImmediate;
const _getCallSites = getCallSites;
const _freeze = Object.freeze;
const _defineProperty = Object.defineProperty;
const _memoryUsage = process.memoryUsage.bind(process);
const _fetch = typeof globalThis.fetch === 'function' ? globalThis.fetch : null;
const _Promise = Promise;
const _allSettled = Promise.allSettled.bind(Promise);
const _race = Promise.race.bind(Promise);
const _byteLength = Buffer.byteLength;

/** How long, after the answer, leftover work gets to finish before the call is called dirty. */
const CLEAN_GRACE_MS = positiveInt(process.env.BLOCKS_CLEAN_GRACE_MS, 200);
const CLEAN_POLL_MS = 10;
/** One envelope per line — the same 1 MB ceiling single-run mode applies to execution.json. */
const MAX_LINE_BYTES = 1024 * 1024;

/**
 * Created by anything — these are a call's own unfinished work, never long-lived pool state.
 * Deliberately NOT here: sockets (TCPWRAP/TLSWRAP/PIPEWRAP — connection pools), and ZLIB, which
 * Node keeps alive after a finished operation until GC (measured on 24.20), so it would make
 * clean calls dirty.
 */
const ALWAYS_UNFINISHED = new Set([
  'HTTPCLIENTREQUEST', 'HTTPINCOMINGMESSAGE',
  'FSREQCALLBACK', 'FSREQPROMISE', 'FILEHANDLECLOSEREQ', 'STATWATCHER', 'FSEVENTWRAP',
  'GETADDRINFOREQWRAP', 'GETNAMEINFOREQWRAP', 'QUERYWRAP', 'TCPCONNECTWRAP', 'PIPECONNECTWRAP',
  'PROCESSWRAP', 'WORKER', 'SHUTDOWNWRAP', 'WRITEWRAP', 'UDPSENDWRAP',
  // Crypto work on the thread pool: its callback would otherwise run inside a later call.
  // Not RANDOMBYTESREQUEST: Node refills its random pool in the background (crypto.randomUUID,
  // and so mongodb and pg) and that request outlives the call — measured, it made clean calls dirty.
  'PBKDF2REQUEST', 'SCRYPTREQUEST', 'RANDOMPRIMEREQUEST', 'CHECKPRIMEREQUEST',
  'HASHREQUEST', 'HMACREQUEST', 'HKDFREQUEST', 'KEYGENREQUEST', 'KEYPAIRGENREQUEST',
  'SECRETKEYGENREQUEST', 'SIGNREQUEST', 'VERIFYREQUEST', 'CIPHERREQUEST', 'DERIVEBITSREQUEST',
  'KEYEXPORTREQUEST',
]);
/** Unfinished only when the function's own code created them. */
const OWN_CODE_ONLY = new Set(['Timeout', 'Immediate']);
/** Node's own timer plumbing: skipped to reach whoever asked for the timer. */
const TIMER_INTERNALS = new Set([
  'node:internal/async_hooks', 'node:internal/timers', 'node:timers', 'node:timers/promises',
  'node:internal/timers/promises',
]);

function positiveInt(raw, fallback) {
  const n = Number(raw);
  return Number.isInteger(n) && n > 0 ? n : fallback;
}

/**
 * Whether the code creating a timer right now is the function's own. Uses util.getCallSites,
 * which — unlike a stack string — no tenant `Error.prepareStackTrace`, frozen
 * `Error.stackTraceLimit` or source map can rewrite (checked on Node 24.20). Past this runtime's
 * frames and Node's timer plumbing, the first frame decides:
 *   - another `node:` module → Node created it on someone's behalf (AbortSignal.timeout, http's
 *     socket timers) → not the function's own;
 *   - under node_modules → a library (pool heartbeats) → not its own;
 *   - under the function root → its own.
 */
function makeOwnCodeTest(functionRoot) {
  const root = functionRoot.endsWith('/') ? functionRoot : functionRoot + '/';
  const runtimeDir = dirname(new URL(import.meta.url).pathname) + '/';
  const pathOf = (name) => (typeof name === 'string' && name.startsWith('file://') ? name.slice(7) : name);
  return function isOwnCode() {
    const sites = _getCallSites(32);
    for (const site of sites) {
      const name = site?.scriptName;
      if (typeof name !== 'string' || name.length === 0) continue;
      if (TIMER_INTERNALS.has(name)) continue;
      const file = pathOf(name);
      if (file.startsWith(runtimeDir)) continue;
      if (name.startsWith('node:')) return false;
      if (file.includes('/node_modules/')) return false;
      return file.startsWith(root);
    }
    return false;
  };
}

export async function runReuse({ functionEntry, describe, patchConsole }) {
  const als = new AsyncLocalStorage();
  const isOwnCode = makeOwnCodeTest(dirname(functionEntry));

  // Every call is identified by an object of its own, not by its run id: a run id can come round
  // again (a redelivery), and an earlier call's leftovers must never pass for the current one's.
  /** asyncId → { tok, type, resource } for tracked work created inside a call. */
  const live = new Map();
  /** call token → number of fetch() calls not yet settled. */
  const inFlightFetch = new Map();
  /** call token → late writer, for a ctx.log used after its call. */
  const lateWriters = new Map();
  /** The call being served right now ({ tok, id, writer }), or null between calls. */
  let current = null;
  /** Code that belongs to an earlier call ran (while a later call, or none, was current). */
  let lateActivity = false;
  /** Every secret value seen by this sandbox, for lines not tied to one call (fatal, late, idle). */
  const allMasked = new Set();
  let dying = false;

  const sandboxWriter = new ProtocolWriter(_stdoutWrite);
  const betweenCalls = new ProtocolWriter(_stdoutWrite, { call: 'none', late: true });
  function armRedaction() {
    const values = [...allMasked];
    sandboxWriter.useRedaction(values);
    betweenCalls.useRedaction(values);
    for (const w of lateWriters.values()) w.useRedaction(values);
  }
  function lateWriter(tok) {
    let w = lateWriters.get(tok);
    if (!w) {
      w = new ProtocolWriter(_stdoutWrite, { call: tok.id, late: true });
      w.useRedaction([...allMasked]);
      lateWriters.set(tok, w);
    }
    return w;
  }

  const hook = createHook({
    // No promise tracking: it is not needed here and costs ~9.5x on every await (Node 24.20).
    trackPromises: false,
    init(asyncId, type, _triggerId, resource) {
      try {
        const tok = als.getStore();
        if (tok === undefined) return;
        if (OWN_CODE_ONLY.has(type)) {
          if (!isOwnCode()) return;
        } else if (!ALWAYS_UNFINISHED.has(type)) {
          return;
        }
        live.set(asyncId, { tok, type, resource });
      } catch {
        // Never throw out of an async hook — Node treats that as fatal. Unsure → count it, so
        // the call is reported dirty rather than silently clean.
        try { live.set(asyncId, { tok: als.getStore(), type, resource }); } catch { /* nothing */ }
      }
    },
    before(asyncId) {
      // Tracked work of an earlier call is about to run while another call (or none) is current.
      const entry = live.get(asyncId);
      if (entry && entry.tok !== current?.tok) lateActivity = true;
    },
    destroy(asyncId) { live.delete(asyncId); },
  });
  hook.enable();

  /**
   * Where console output goes. During a call: that call's writer, whatever async context the line
   * comes from — a callback a library fires on a socket opened by an earlier call is still this
   * call's legitimate work, and must neither be lost nor make the sandbox dirty. Between calls:
   * the sandbox's background writer. (ctx.log is exact; see below.)
   */
  function consoleWriter() {
    return current ? current.writer : betweenCalls;
  }

  if (_fetch) {
    const trackedFetch = function fetch(...args) {
      const tok = als.getStore();
      if (tok === undefined) return _fetch(...args);
      inFlightFetch.set(tok, (inFlightFetch.get(tok) ?? 0) + 1);
      const done = () => {
        const n = (inFlightFetch.get(tok) ?? 1) - 1;
        if (n <= 0) inFlightFetch.delete(tok); else inFlightFetch.set(tok, n);
      };
      let p;
      try { p = _fetch(...args); } catch (err) { done(); throw err; }
      p.then(done, done);
      return p;
    };
    _defineProperty(globalThis, 'fetch', { value: trackedFetch, writable: true, configurable: true });
  }

  /** What of the call `tok` is still unfinished, by type. */
  function unfinished(tok) {
    const out = [];
    for (const r of live.values()) {
      if (r.tok !== tok) continue;
      // A fired or cleared timer has already left `live` through the destroy hook.
      out.push(r.type === 'Timeout' && r.resource?._repeat ? 'Interval' : r.type);
    }
    const f = inFlightFetch.get(tok);
    if (f) out.push(`fetch x${f}`);
    return out;
  }

  async function settleAndCheck(tok) {
    await new _Promise((r) => _setImmediate(r));
    const until = _now() + CLEAN_GRACE_MS;
    let left = unfinished(tok);
    while (left.length > 0 && _now() < until) {
      await new _Promise((r) => _setTimeout(r, CLEAN_POLL_MS).unref?.());
      left = unfinished(tok);
    }
    return left;
  }

  /** The sandbox cannot go on: one fatal line (masked), then exit. Only ever once. */
  function fatal(code, message) {
    if (dying) return;
    dying = true;
    sandboxWriter.control({ t: 'fatal', code, message: String(message ?? '').slice(0, 8192) });
    const exitCode = code === CODE.RUNTIME_START_FAILED ? EXIT.BOOTSTRAP_ERROR : EXIT.USER_ERROR;
    _setTimeout(() => _exit(exitCode), 50).unref?.();
    try { _stdoutWrite('', () => _exit(exitCode)); } catch { _exit(exitCode); }
  }

  patchConsole(consoleWriter);

  process.on('unhandledRejection', (reason) => onCrash('unhandled rejection', reason));
  process.on('uncaughtException', (err) => onCrash(null, err));
  process.on('SIGTERM', () => _exit(143));

  /**
   * A crash fails the current call — but its message and stack are shown only when the crash
   * came from that call's own work. Work an earlier call (or module code) left behind must not
   * hand its error text, which may quote that caller's data, to this caller.
   */
  function onCrash(prefix, err) {
    if (dying) return;
    const tok = als.getStore();
    if (current && !current.answered) {
      if (tok !== undefined && tok === current.tok) {
        const d = describe(err);
        current.writer.failure(CODE.USER_RUNTIME_ERROR, prefix ? `${prefix}: ${d.message}` : d.message, d.stack);
      } else {
        current.writer.failure(CODE.USER_RUNTIME_ERROR,
          'the sandbox crashed while running work that did not belong to this call');
      }
    }
    if (current) {
      sandboxWriter.control({ t: 'idle', call: current.id, clean: false, leftovers: ['crash'], late: lateActivity });
    }
    fatal(CODE.USER_RUNTIME_ERROR, 'the function crashed outside its handler');
  }

  // --- load the function once -------------------------------------------------
  let handler;
  try {
    const mod = await import(functionEntry);
    handler = mod?.default;
  } catch (err) {
    return fatal(CODE.USER_RUNTIME_ERROR, `the function module failed to load: ${describe(err).message}`);
  }
  if (typeof handler !== 'function') {
    return fatal(CODE.USER_RUNTIME_ERROR,
      'the function must export a default function: export default async function (input, ctx) { … }');
  }
  sandboxWriter.control({ t: 'ready', at: _now() });

  // --- serve calls, one at a time -----------------------------------------------
  const lines = createInterface({ input: process.stdin, crlfDelay: Infinity });
  for await (const line of lines) {
    if (dying) return;
    if (line.length === 0) continue;
    if (_byteLength(line, 'utf8') > MAX_LINE_BYTES) {
      return fatal(CODE.RUNTIME_START_FAILED, 'a call envelope exceeded the 1 MB ceiling');
    }
    let envelope;
    try {
      envelope = parseEnvelope(line);
    } catch (err) {
      return fatal(CODE.RUNTIME_START_FAILED, `cannot read a call envelope: ${describe(err).message}`);
    }
    const keepGoing = await serve(envelope);
    if (!keepGoing) return;
  }
  _exit(EXIT.OK);

  /** Runs one call to its `idle` line. False when the sandbox must not serve another. */
  async function serve(envelope) {
    const id = envelope.run.id;
    const tok = _freeze({ id });
    for (const v of envelope.maskedValues) allMasked.add(v);
    armRedaction();
    const writer = new ProtocolWriter(_stdoutWrite, { call: id });
    writer.useRedaction(envelope.maskedValues);
    // lateActivity is NOT reset here: code of an earlier call that wakes up when the runner
    // unpauses this sandbox for the next call must still make that next call dirty.
    const waits = [];
    let open = true;
    current = { tok, id, writer, answered: false };

    const token = envelope.blocks.accessToken;
    const blocks = _freeze(Object.create(Object.prototype, {
      accessToken: {
        enumerable: true,
        get() {
          // One call runs at a time, so "this call is still open" is the whole rule. A ctx kept
          // from an earlier call reads nothing, and its use is a sign that call left work behind.
          if (open) return token;
          if (current) lateActivity = true;
          throw new Error('ctx.blocks.accessToken was read after its call ended');
        },
      },
    }));
    /** ctx.log is bound to its own call, whatever async context it is called from. */
    const logTo = (level) => (msg, data) => {
      if (current && current.tok === tok) return current.writer.log(level, msg, data);
      if (current) lateActivity = true;
      lateWriter(tok).log(level, msg, data);
    };
    const log = _freeze({ debug: logTo('debug'), info: logTo('info'), warn: logTo('warn'), error: logTo('error') });
    const ctx = _freeze({
      context: envelope.context,
      blocks,
      env: envelope.env,
      run: envelope.run,
      log,
      /** Finish `promise` after the answer is sent; the sandbox is not reused until it settles. */
      waitUntil(promise) {
        if (!open) throw new Error('ctx.waitUntil was called after its call ended');
        waits.push(_Promise.resolve(promise));
      },
    });

    const timeoutMs = envelope.limits.timeoutMs;
    const startedAt = _now();
    let deadlineTimer = null;
    const deadline = timeoutMs
      ? new _Promise((_, reject) => {
          deadlineTimer = _setTimeout(() => reject(Object.assign(new Error(
            `the function exceeded its ${timeoutMs} ms time limit`), { __timeout: true })), timeoutMs);
          deadlineTimer.unref?.();
        })
      : null;

    writer.started();
    try {
      const run = als.run(tok, () => handler(envelope.input, ctx));
      const value = deadline ? await _race([run, deadline]) : await run;
      const problem = writer.result(value);
      if (problem === CODE.RESULT_TOO_LARGE) {
        writer.failure(CODE.RESULT_TOO_LARGE, 'the result exceeds the 5242880 byte ceiling');
      } else if (problem === CODE.RESULT_NOT_SERIALIZABLE) {
        writer.failure(CODE.RESULT_NOT_SERIALIZABLE,
          'the returned value cannot be serialized to JSON (circular reference, BigInt or a throwing toJSON)');
      }
    } catch (err) {
      const d = describe(err);
      writer.failure(err?.__timeout ? CODE.TIMED_OUT : CODE.USER_RUNTIME_ERROR, d.message,
        err?.__timeout ? undefined : d.stack);
      if (err?.__timeout) {
        // The handler is still running and cannot be stopped from here: this sandbox is done.
        current.answered = true;
        sandboxWriter.control({ t: 'idle', call: id, clean: false, leftovers: ['timeout'], late: lateActivity });
        fatal(CODE.TIMED_OUT, `call ${id} exceeded its time limit`);
        return false;
      }
    }
    current.answered = true;

    // Work handed to ctx.waitUntil runs on after the answer, inside what is left of the deadline.
    // Repeated until no new work is added, so waitUntil called from inside waitUntil work is
    // waited for too.
    let settled = 0;
    while (settled < waits.length) {
      const batch = waits.slice(settled);
      settled = waits.length;
      const rest = timeoutMs ? timeoutMs - (_now() - startedAt) : null;
      const all = _allSettled(batch).then(() => 'done');
      const outcome = rest === null
        ? await all
        : await _race([all, new _Promise((r) => _setTimeout(() => r('late'), Math.max(0, rest)).unref?.())]);
      if (outcome === 'late') {
        sandboxWriter.control({ t: 'idle', call: id, clean: false, leftovers: ['waitUntil'], late: lateActivity });
        fatal(CODE.TIMED_OUT, `work passed to ctx.waitUntil in call ${id} exceeded the time limit`);
        return false;
      }
    }
    if (deadlineTimer) _clearTimeout(deadlineTimer);

    const leftovers = await settleAndCheck(tok);
    open = false;
    current = null;
    const clean = leftovers.length === 0 && !lateActivity;
    let rssBytes = null;
    try { rssBytes = _memoryUsage().rss; } catch { /* reported as null */ }
    sandboxWriter.control({ t: 'idle', call: id, clean, leftovers, late: lateActivity, rssBytes });
    // A dirty sandbox is destroyed by the runner; a clean one starts the next call from scratch.
    lateActivity = false;
    return true;
  }
}
