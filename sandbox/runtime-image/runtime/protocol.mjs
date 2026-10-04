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
  #sink;
  /** `[{ raw, escaped }]` for every secret-backed value, longest first. */
  #secrets = [];

  constructor(sink = _stdoutWrite) {
    this.#sink = sink;
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
    const entry = { t: 'log', ts: isoNow(), level: safeLevel, msg: clip(msg, LIMITS.MESSAGE_CHARS) };
    if (data !== undefined) entry.data = data;

    let line;
    try {
      line = _stringify(entry);
    } catch {
      // Unserializable data (BigInt, circular, a hostile toJSON): keep the message.
      line = _stringify({ t: 'log', ts: entry.ts, level: safeLevel, msg: entry.msg,
                          data: { _unserializable: true } });
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
    this.#emit({ t: 'truncated', reason });
  }

  /** The single success line. Returns null on success, or an error code. */
  result(value) {
    if (this.#resultWritten) return null;

    let payload;
    try {
      payload = _stringify({ t: 'result', ok: true, value: value === undefined ? null : value });
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

  /** The single failure line. Always written, even after the log budget is exhausted. */
  failure(code, message, stack) {
    if (this.#resultWritten) return;
    this.#resultWritten = true;
    const entry = { t: 'result', ok: false, code, message: clip(String(message ?? ''), LIMITS.MESSAGE_CHARS) };
    if (stack) entry.stack = clip(String(stack), LIMITS.STACK_CHARS);
    if (!this.#emit(entry)) {
      this.#sink(_stringify({ t: 'result', ok: false, code, message: 'unrepresentable error' }) + '\n');
    }
  }
}

export const _internals = _freeze({ clip, isoNow, LEVELS, _isArray, _min, MIN_REDACT_CHARS, REDACTED });
