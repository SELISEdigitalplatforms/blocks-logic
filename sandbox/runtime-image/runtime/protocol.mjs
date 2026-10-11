// protocol.mjs — the sandbox side of the stdout protocol in plan/PROTOCOL.md.
//
// Trusted code. It runs in the same process as the tenant's function, so every primitive it
// depends on is captured here, at module load, before any tenant code is imported. A
// function that later replaces JSON.stringify, Array.prototype.push or
// process.stdout.write cannot make this module lie: the writer keeps its own references.
// This is hardening, not a boundary — the real boundary is gVisor plus the runner's
// out-of-process byte caps, which apply whatever the sandbox emits.

const _stringify = JSON.stringify;
const _now = Date.now;
const _toISOString = Date.prototype.toISOString;
const _stdoutWrite = process.stdout.write.bind(process.stdout);
const _freeze = Object.freeze;
const _isArray = Array.isArray;
const _min = Math.min;
const _byteLength = Buffer.byteLength;
const _TextDecoder = globalThis.TextDecoder;
const _Uint8Array = Uint8Array;
const _asyncIterator = Symbol.asyncIterator;

/**
 * Replaces a secret's value wherever it appears in anything this process writes.
 *
 * A value below this length is left alone on purpose: redacting a two-character string would
 * replace those two characters everywhere in every line and destroy the logs, and a value that
 * short carries no secrecy to protect. Anything at or above it is masked in full.
 */
const MIN_REDACT_CHARS = 4;
const REDACTED = '[redacted]';

export const LIMITS = _freeze({
  LOG_BYTES: 1024 * 1024,     // 1 MB of log payload
  LOG_LINES: 10000,           // or 10 000 lines, whichever comes first
  RESULT_BYTES: 5 * 1024 * 1024,
  MESSAGE_CHARS: 8192,        // per-line message cap, so one line cannot eat the budget
  STACK_CHARS: 8192,
  // Streaming (F-5): chunk lines of one call, encoded. With the 1 MB of logs and the kept text in
  // the result line this stays inside the runner's per-call output ceiling (2 × logs + result).
  STREAM_BYTES: 3 * 1024 * 1024,
  STREAM_KEEP_CHARS: 256 * 1024, // the start of the streamed text, kept as the run's result
});

export const EXIT = _freeze({
  OK: 0,             // the handler returned a value
  USER_ERROR: 10,    // the tenant's code failed
  BOOTSTRAP_ERROR: 20, // the runtime could not get far enough to run it
});

// Error codes, as listed in plan/PROTOCOL.md.
export const CODE = _freeze({
  USER_RUNTIME_ERROR: 'USER_RUNTIME_ERROR',
  RUNTIME_START_FAILED: 'RUNTIME_START_FAILED',
  RESULT_TOO_LARGE: 'RESULT_TOO_LARGE',
  RESULT_NOT_SERIALIZABLE: 'RESULT_NOT_SERIALIZABLE',
  TIMED_OUT: 'TIMED_OUT',
});

const LEVELS = _freeze(['debug', 'info', 'warn', 'error']);

function isoNow() {
  return _toISOString.call(new Date(_now()));
}

function clip(value, max) {
  if (typeof value !== 'string') return value;
  return value.length > max ? value.slice(0, max) + '…[clipped]' : value;
}

/**
 * Writes protocol lines to stdout and enforces the log budget in-process.
 * One `truncated` event is emitted the first time a ceiling is hit; nothing is written
 * afterwards except the final `result` line, which is never suppressed.
 */
export class ProtocolWriter {
  #bytes = 0;
  #lines = 0;
  #truncated = false;
  #resultWritten = false;
  #startedWritten = false;
  #streamBytes = 0;
  #sink;
  /** `[{ raw, escaped }]` for every secret-backed value, longest first. */
  #secrets = [];
  /**
   * Reuse mode only: the call this writer speaks for, stamped on every line so the runner can
   * tell one call's output from the next one's in a sandbox that serves many. `late` marks a
   * writer for output that arrived after its call had already been answered. Both absent in
   * single-run mode, so that protocol is byte-for-byte what it was.
   */
  #call;
  #late;

  constructor(sink = _stdoutWrite, { call, late } = {}) {
    this.#sink = sink;
    this.#call = typeof call === 'string' && call.length > 0 ? call : undefined;
    this.#late = late === true;
  }

  get call() { return this.#call; }

  #tag(obj) {
    if (this.#call !== undefined) obj.call = this.#call;
    if (this.#late) obj.late = true;
    return obj;
  }

  /**
   * Masks `values` in every line written from here on. Called once by the bootstrap, after the
   * envelope is parsed and **before** the tenant's module is imported, so there is no window in
   * which a secret could be logged unmasked.
   *
   * Scrubbing happens on the serialized line rather than on the message and data separately: a
   * secret can sit at any depth of `data`, inside an error's stack, or spliced into a string, and
   * one pass over the finished JSON catches every one of those without walking the object. Both
   * the raw value and its JSON-escaped form are replaced, because a value containing a quote or a
   * backslash reaches the line escaped.
   */
  useRedaction(values) {
    const seen = new Set();
    const secrets = [];
    for (const value of values ?? []) {
      if (typeof value !== 'string' || value.length < MIN_REDACT_CHARS || seen.has(value)) continue;
      seen.add(value);
      const escaped = _stringify(value).slice(1, -1);
      secrets.push({ raw: value, escaped: escaped === value ? null : escaped });
    }
    // Longest first: a secret that contains another (a token and its prefix) must be masked as
    // a whole rather than leaving the tail of the longer one exposed.
    secrets.sort((a, b) => b.raw.length - a.raw.length);
    this.#secrets = secrets;
  }

  #scrub(line) {
    if (this.#secrets.length === 0) return line;
    let out = line;
    for (const secret of this.#secrets) {
      if (out.includes(secret.raw)) out = out.split(secret.raw).join(REDACTED);
      if (secret.escaped && out.includes(secret.escaped)) {
        out = out.split(secret.escaped).join(REDACTED);
      }
    }
    return out;
  }

  get truncated() { return this.#truncated; }
  get lineCount() { return this.#lines; }
  get byteCount() { return this.#bytes; }
  get resultWritten() { return this.#resultWritten; }

  #emit(obj) {
    let line;
    try {
      line = this.#scrub(_stringify(obj)) + '\n';
    } catch {
      return false; // never let a serialization problem in a log line kill the run
    }
    this.#sink(line);
    return true;
  }

  /** A log line from ctx.log or a patched console method. */
  log(level, msg, data) {
    if (this.#truncated) return;

    const safeLevel = LEVELS.indexOf(level) >= 0 ? level : 'info';
    const entry = this.#tag({ t: 'log', ts: isoNow(), level: safeLevel, msg: clip(msg, LIMITS.MESSAGE_CHARS) });
    if (data !== undefined) entry.data = data;

    let line;
    try {
      line = _stringify(entry);
    } catch {
      // Unserializable data (BigInt, circular, a hostile toJSON): keep the message.
      line = _stringify(this.#tag({ t: 'log', ts: entry.ts, level: safeLevel, msg: entry.msg,
                          data: { _unserializable: true } }));
    }

    // Scrub before measuring: the redacted line is the one that gets written, so it is the one
    // the budget has to account for.
    line = this.#scrub(line);

    const size = _byteLength(line, 'utf8') + 1;
    if (this.#lines + 1 > LIMITS.LOG_LINES) return this.#truncate('log_lines');
    if (this.#bytes + size > LIMITS.LOG_BYTES) return this.#truncate('log_bytes');

    this.#lines += 1;
    this.#bytes += size;
    this.#sink(line + '\n');
  }

  #truncate(reason) {
    if (this.#truncated) return;
    this.#truncated = true;
    this.#emit(this.#tag({ t: 'truncated', reason }));
  }

  /**
   * The one line that says the function's own code is about to begin.
   *
   * Everything before it — the gVisor sandbox booting, Node starting, importing a dependency tree —
   * is the platform's time, not the tenant's. Without this line the runner cannot tell the two
   * apart, so it has to arm its kill timer from container start and allow a fixed grace for the
   * boot. A function with a thousand dependencies then gets killed as TIMED_OUT while its handler
   * is still well inside its own budget, which reads as the tenant's fault and is not.
   *
   * Written outside the log budget: it is accounting, not output, and it must not be the line that
   * a chatty function pushes over the limit.
   */
  started() {
    if (this.#startedWritten) return;
    this.#startedWritten = true;
    this.#sink(_stringify(this.#tag({ t: 'started', at: _now() })) + '\n');
  }

  /** The single success line. Returns null on success, or an error code. */
  result(value) {
    if (this.#resultWritten) return null;

    let payload;
    try {
      payload = _stringify(this.#tag({ t: 'result', ok: true, value: value === undefined ? null : value }));
    } catch {
      // Circular structures, BigInt, or a toJSON that throws.
      return CODE.RESULT_NOT_SERIALIZABLE;
    }
    if (payload === undefined) return CODE.RESULT_NOT_SERIALIZABLE; // e.g. a bare function

    const size = _byteLength(payload, 'utf8');
    if (size > LIMITS.RESULT_BYTES) return CODE.RESULT_TOO_LARGE;

    this.#resultWritten = true;
    this.#sink(payload + '\n');
    return null;
  }

  /**
   * Reuse mode only: a control line (`ready`, `idle`, `fatal`) written as given — no call tag, no
   * log budget — but through this writer's redaction, so a crash message that quotes a secret is
   * masked like any log line would be.
   */
  /**
   * One piece of a streamed answer (F-5). Not redacted, exactly like the result: it is the
   * function's answer to its caller. False once the stream budget is spent — nothing is written.
   */
  chunk(text) {
    if (typeof text !== 'string' || text.length === 0) return true;
    const line = _stringify(this.#tag({ t: 'chunk', data: text })) + '\n';
    const size = _byteLength(line, 'utf8');
    if (this.#streamBytes + size > LIMITS.STREAM_BYTES) return false;
    this.#streamBytes += size;
    this.#sink(line);
    return true;
  }

  control(obj) {
    if (!this.#emit(obj)) {
      this.#sink(_stringify({ t: obj?.t ?? 'fatal', message: 'unrepresentable control line' }) + '\n');
    }
  }

  /** The single failure line. Always written, even after the log budget is exhausted. */
  failure(code, message, stack) {
    if (this.#resultWritten) return;
    this.#resultWritten = true;
    const entry = this.#tag({ t: 'result', ok: false, code, message: clip(String(message ?? ''), LIMITS.MESSAGE_CHARS) });
    if (stack) entry.stack = clip(String(stack), LIMITS.STACK_CHARS);
    if (!this.#emit(entry)) {
      this.#sink(_stringify(this.#tag({ t: 'result', ok: false, code, message: 'unrepresentable error' })) + '\n');
    }
  }
}

/** A handler's answer that is streamed rather than returned whole: any async iterable. */
export function isStream(value) {
  return value !== null && (typeof value === 'object' || typeof value === 'function')
    && typeof value[_asyncIterator] === 'function';
}

/**
 * Writes a streamed answer as `chunk` lines, piece by piece as the handler yields them, and
 * returns its start (STREAM_KEEP_CHARS) as the run's result. A string is sent as it is; bytes
 * (Uint8Array, Buffer, a fetch body) as UTF-8 text; anything else as one JSON line (NDJSON).
 * Over the stream budget it throws RESULT_TOO_LARGE: what was sent stays sent.
 */
export async function pumpStream(iterable, writer) {
  const decoder = new _TextDecoder();
  let kept = '';
  const send = (text) => {
    if (!text) return;
    if (!writer.chunk(text)) {
      throw Object.assign(new Error(`the streamed answer exceeds the ${LIMITS.STREAM_BYTES} byte ceiling`),
        { __code: CODE.RESULT_TOO_LARGE });
    }
    if (kept.length < LIMITS.STREAM_KEEP_CHARS) kept += text.slice(0, LIMITS.STREAM_KEEP_CHARS - kept.length);
  };
  for await (const part of iterable) {
    if (part === null || part === undefined) continue;
    if (typeof part === 'string') send(part);
    else if (part instanceof _Uint8Array) send(decoder.decode(part, { stream: true }));
    else send(_stringify(part) + '\n');
  }
  send(decoder.decode());
  return kept;
}

export const _internals = _freeze({ clip, isoNow, LEVELS, _isArray, _min, MIN_REDACT_CHARS, REDACTED });
