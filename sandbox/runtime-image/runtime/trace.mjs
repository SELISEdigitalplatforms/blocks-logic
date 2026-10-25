// trace.mjs — adds the run's `traceparent` to fetch() calls that go to a Blocks host.
//
// The runner puts its `Function::Run` span's traceparent and the list of hosts it may go to in the
// envelope (`trace`). A request whose host is on the list (or a subdomain of one) and that carries no
// `traceparent` yet gets one, so the call the function makes joins the run's trace. Any other host
// is left alone: a trace id must not go to every third party.
//
// Trusted code: fetch, Headers, Request and URL are captured at module load, before any tenant
// code is imported, so replacing the globals later changes nothing here.

const _Headers = globalThis.Headers;
const _Request = globalThis.Request;
const _URL = globalThis.URL;
const _toLowerCase = String.prototype.toLowerCase;
const _endsWith = String.prototype.endsWith;

const TRACEPARENT = 'traceparent';

/** The URL's host in lower case, or null when `input` is no usable URL (the real fetch then reports it). */
function hostOf(input) {
  try {
    const url = input instanceof _Request ? input.url : String(input);
    return _toLowerCase.call(new _URL(url).hostname);
  } catch {
    return null;
  }
}

function onList(host, hosts) {
  return hosts.some((h) => host === h || _endsWith.call(host, '.' + h));
}

/** Headers given in `init` replace the Request's own, so they are the effective ones when present. */
function effectiveHeaders(input, init) {
  if (init?.headers !== undefined) return init.headers;
  return input instanceof _Request ? input.headers : undefined;
}

/**
 * Wraps `baseFetch`. `getTrace()` is asked for the trace of the code that calls fetch right now
 * (`{ traceparent, hosts }` or null), so reuse mode can answer per call.
 */
export function withTraceparent(baseFetch, getTrace) {
  return function fetch(input, init) {
    const trace = getTrace();
    if (!trace || !trace.traceparent || trace.hosts.length === 0) return baseFetch(input, init);

    const host = hostOf(input);
    if (host === null || !onList(host, trace.hosts)) return baseFetch(input, init);

    let headers;
    try {
      headers = new _Headers(effectiveHeaders(input, init));
    } catch {
      return baseFetch(input, init); // unusable headers: let the real fetch report them
    }
    if (headers.has(TRACEPARENT)) return baseFetch(input, init);

    headers.set(TRACEPARENT, trace.traceparent);
    return baseFetch(input, { ...init, headers });
  };
}
