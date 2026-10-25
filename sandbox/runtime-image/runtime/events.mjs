// events.mjs — `ctx.events`: the function's outbox (function events, phase 1).
//
// `ctx.events.add(type, payload)` only BUFFERS, synchronously, in this process. Nothing leaves the
// sandbox during the call: the buffer rides on the call's success `result` line, and the platform
// sends it only after the run has succeeded (a failed call drops it). The runner validates the list
// again out of process, so the checks here are for the developer's benefit — a clear error at the
// line that added the bad event — not the security boundary (the sandbox is untrusted).
//
// Trusted code: primitives are captured at module load, before any tenant code is imported.

import { LIMITS } from './protocol.mjs';

const _stringify = JSON.stringify;
const _freeze = Object.freeze;
const _isArray = Array.isArray;
const _getPrototypeOf = Object.getPrototypeOf;
const _objectPrototype = Object.prototype;
const _keys = Object.keys;
const _byteLength = Buffer.byteLength;
const _push = Array.prototype.push;
const _slice = Array.prototype.slice;

/** The event types a function can be allowed to send today. */
export const EVENT_TYPES = _freeze(['SendMail', 'FunctionEvent']);

/** The name of a `FunctionEvent` (also checked by the runner and the Worker). */
export const EVENT_NAME_PATTERN = /^[a-z][a-z0-9_.-]{2,63}$/;

const isPlainObject = (v) => {
  if (v === null || typeof v !== 'object' || _isArray(v)) return false;
  const proto = _getPrototypeOf(v);
  return proto === _objectPrototype || proto === null;
};

/**
 * The outbox of one call. `allowed` is the envelope's `events.allowed`. Returns the frozen
 * `ctx.events`, plus `take()` (the buffered events, for the success line) and `close()` (the answer
 * is out: from now on `add` throws).
 */
export function createEventBuffer({ runId, attempt, allowed }) {
  const permitted = new Set((allowed ?? []).filter((t) => EVENT_TYPES.includes(t)));
  const buffered = [];
  let totalBytes = 0;
  let open = true;

  /** Checks the payload for `type` and returns what is stored (a FunctionEvent gets its `data` filled in). */
  function checked(type, payload) {
    if (!isPlainObject(payload)) throw new Error(`the payload of a ${type} event must be a plain object`);
    if (type !== 'FunctionEvent') return payload;

    for (const key of _keys(payload)) {
      if (key !== 'name' && key !== 'data') {
        throw new Error(`a FunctionEvent payload has only "name" and "data", not "${key}"`);
      }
    }
    if (typeof payload.name !== 'string' || !EVENT_NAME_PATTERN.test(payload.name)) {
      throw new Error('a FunctionEvent name must match ^[a-z][a-z0-9_.-]{2,63}$ (lowercase letters, digits, . _ -)');
    }
    return { name: payload.name, data: payload.data === undefined ? null : payload.data };
  }

  const events = _freeze({
    add(type, payload) {
      if (!open) throw new Error('ctx.events.add was called after its call ended');
      if (typeof type !== 'string' || !permitted.has(type)) {
        throw new Error(`event type "${String(type)}" is not allowed for this function: add it in the function's Events tab`);
      }
      const value = checked(type, payload);
      if (buffered.length >= LIMITS.EVENTS_MAX) {
        throw new Error(`a call can add at most ${LIMITS.EVENTS_MAX} events`);
      }

      // Serialized now, so a later change to the object does not change what is sent.
      let json;
      try {
        json = _stringify(value);
      } catch (err) {
        throw new Error(`the payload of a ${type} event cannot be serialized to JSON: ${err?.message ?? err}`);
      }
      if (typeof json !== 'string') throw new Error(`the payload of a ${type} event cannot be serialized to JSON`);

      const size = _byteLength(json, 'utf8');
      if (size > LIMITS.EVENT_BYTES) {
        throw new Error(`a ${type} event payload is ${size} bytes; the limit is ${LIMITS.EVENT_BYTES} bytes`);
      }
      if (totalBytes + size > LIMITS.EVENTS_TOTAL_BYTES) {
        throw new Error(`the events of one call can hold ${LIMITS.EVENTS_TOTAL_BYTES} bytes in total`);
      }

      const id = `${runId}-${attempt}-${buffered.length + 1}`;
      _push.call(buffered, { id, type, json });
      totalBytes += size;
      return _freeze({ id });
    },
  });

  return {
    events,
    /** The buffered events as `{ id, type, json }` (json = the payload, already serialized). */
    take: () => _slice.call(buffered),
    close() { open = false; },
  };
}
