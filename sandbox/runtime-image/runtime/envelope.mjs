// envelope.mjs — validation of the execution envelope (spec §15, §16, §19).
//
// The envelope is produced by trusted infrastructure, so this is not a trust boundary; it
// is a contract check that turns a malformed or stale envelope into a clean
// RUNTIME_START_FAILED instead of an obscure crash inside the tenant's handler.
//
// It is also where the authoritative identity fields are pinned: whatever the envelope
// says becomes a frozen `ctx.context`, and nothing the tenant does can redefine it.

const _freeze = Object.freeze;
const _isArray = Array.isArray;

export class EnvelopeError extends Error {
  constructor(message) { super(message); this.name = 'EnvelopeError'; }
}

const fail = (m) => { throw new EnvelopeError(m); };

const isPlainObject = (v) => v !== null && typeof v === 'object' && !_isArray(v);

function stringOrNull(value, field) {
  if (value === undefined || value === null) return null;
  if (typeof value !== 'string') fail(`${field} must be a string or null`);
  return value;
}

function stringArray(value, field) {
  if (value === undefined || value === null) return _freeze([]);
  if (!_isArray(value)) fail(`${field} must be an array`);
  for (const v of value) if (typeof v !== 'string') fail(`${field} must contain strings only`);
  return _freeze([...value]);
}

/**
 * `ctx.env` holds non-secret configuration only. Values are coerced to a JSON-safe scalar
 * shape; anything else is rejected rather than silently passed through, so a function can
 * rely on `typeof ctx.env.X`.
 */
function envMap(value) {
  if (value === undefined || value === null) return _freeze(Object.create(null));
  if (!isPlainObject(value)) fail('env must be an object');
  const out = Object.create(null);
  for (const [k, v] of Object.entries(value)) {
    if (typeof k !== 'string' || k.length === 0) fail('env keys must be non-empty strings');
    const t = typeof v;
    if (v === null || t === 'string' || t === 'number' || t === 'boolean') out[k] = v;
    else fail(`env.${k} must be a string, number, boolean or null`);
  }
  return _freeze(out);
}

/** Parses and validates the envelope, returning the frozen pieces the bootstrap needs. */
export function parseEnvelope(raw) {
  let doc;
  try {
    doc = JSON.parse(raw);
  } catch (err) {
    fail(`envelope is not valid JSON: ${err.message}`);
  }
  if (!isPlainObject(doc)) fail('envelope must be a JSON object');

  // --- run -------------------------------------------------------------------
  const run = doc.run;
  if (!isPlainObject(run)) fail('envelope.run is required');
  if (typeof run.id !== 'string' || run.id.length === 0) fail('envelope.run.id is required');

  let invokedBy = _freeze({ type: 'http', id: null });
  if (run.invokedBy !== undefined && run.invokedBy !== null) {
    if (!isPlainObject(run.invokedBy)) fail('envelope.run.invokedBy must be an object');
    invokedBy = _freeze({
      type: stringOrNull(run.invokedBy.type, 'run.invokedBy.type') ?? 'http',
      id: stringOrNull(run.invokedBy.id, 'run.invokedBy.id'),
    });
  }

  const frozenRun = _freeze({
    id: run.id,
    functionId: stringOrNull(run.functionId, 'run.functionId'),
    version: run.version === undefined || run.version === null ? null : Number(run.version),
    attempt: run.attempt === undefined || run.attempt === null ? 1 : Number(run.attempt),
    invokedBy,
  });
  if (frozenRun.version !== null && !Number.isFinite(frozenRun.version)) fail('run.version must be a number');
  if (!Number.isFinite(frozenRun.attempt)) fail('run.attempt must be a number');

  // --- context ---------------------------------------------------------------
  // Authoritative identity. Absent fields become explicit nulls/empties rather than
  // `undefined`, so `ctx.context.userId === null` is always a meaningful test (spec §18).
  const c = doc.context;
  if (c !== undefined && c !== null && !isPlainObject(c)) fail('envelope.context must be an object');
  const ctxContext = _freeze({
    tenantId: stringOrNull(c?.tenantId, 'context.tenantId'),
    userId: stringOrNull(c?.userId, 'context.userId'),
    organizationId: stringOrNull(c?.organizationId, 'context.organizationId'),
    roles: stringArray(c?.roles, 'context.roles'),
    permissions: stringArray(c?.permissions, 'context.permissions'),
    email: stringOrNull(c?.email, 'context.email'),
    isAuthenticated: c?.isAuthenticated === true,
    impersonated: c?.impersonated === true,
    applicationDomain: stringOrNull(c?.applicationDomain, 'context.applicationDomain'),
  });

  // --- limits ----------------------------------------------------------------
  // Advisory copies of what the runner already enforces out of process. The soft deadline
  // is the only one the bootstrap acts on.
  const l = doc.limits;
  if (l !== undefined && l !== null && !isPlainObject(l)) fail('envelope.limits must be an object');
  const timeoutMs = l?.timeoutMs === undefined || l?.timeoutMs === null ? null : Number(l.timeoutMs);
  if (timeoutMs !== null && (!Number.isFinite(timeoutMs) || timeoutMs <= 0)) {
    fail('limits.timeoutMs must be a positive number');
  }

  // --- maskedEnv -------------------------------------------------------------
  // The env keys whose values came from a secret. The bootstrap turns these into the redaction
  // list before any tenant code is imported. An envelope without the field simply masks nothing,
  // so an older control plane keeps working — it just has no secrets to mask.
  const env = envMap(doc.env);
  const maskedEnv = stringArray(doc.maskedEnv, 'maskedEnv');

  return _freeze({
    run: frozenRun,
    context: ctxContext,
    env,
    /** The values to mask, resolved here so the bootstrap never has to look them up itself. */
    maskedValues: _freeze(maskedEnv.map((key) => env[key]).filter((v) => typeof v === 'string')),
    input: doc.input === undefined ? null : doc.input,
    limits: _freeze({ timeoutMs }),
  });
}
