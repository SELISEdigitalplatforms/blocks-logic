/**
 * Shown only until `GetLimits` resolves — the server is the source of these numbers, not this file.
 * Mirrors `FunctionLimits.Ceiling` so the first paint matches what arrives a moment later.
 */
export const DEFAULT_LIMITS_OPTIONS = {
  cpuMillicores: 100,
  memoryMb: 128,
  timeoutSeconds: 30,
  concurrency: 10,
  attempts: 2,
  retryDelaySeconds: 5,
  showRateLimits: false,
} as const;

/** Not configurable — enforced by the runner whatever the caller asks for. */
export const HARD_CAPS = [
  // CPU used to be a choice. It is fixed now: each call is a fresh container, so Node's boot and
  // module import are paid every time and are pure CPU, and a smaller share would only slow that
  // down — admission counts memory and slots, never CPU, so a lower value frees nothing.
  { label: "CPU", value: "100m per run" },
  { label: "Input", value: "Size-limited; a larger request is refused" },
  { label: "Result", value: "Size-limited; return a reference for large data" },
  { label: "Logs", value: "Capped per run; lines past the cap are dropped" },
  { label: "Temp disk", value: "Small /tmp only; files can't be executed" },
  { label: "Processes", value: "Capped per sandbox" },
] as const;

/** The "What the sandbox gives you" reference in the Code tab's right rail. */
export const SANDBOX_CTX_DOCS = [
  {
    name: "input",
    description:
      "The request: { method, path, query, headers, body }. path is whatever followed /fn/{id}; body is the parsed JSON (or text) of a POST, null on a GET. From a workflow node it is the previous node's output instead.",
  },
  {
    name: "fetch(url)",
    description:
      "Standard fetch. Public internet is reachable; Blocks internals and private networks are not routed.",
  },
  {
    name: "ctx.env.NAME",
    description: "Your variables, as plain strings. Snapshotted at deploy. A variable bound to a secret arrives as its real value, masked in logs.",
  },
  {
    name: "ctx.context",
    description:
      "tenantId, userId, roles, permissions, isAuthenticated. Empty identity on a public call — never a privileged token.",
  },
  {
    name: "await ctx.blocks.getAccessToken()",
    description:
      "The caller's Blocks token: pass it as accessToken to @seliseblocks/client, with ctx.context.tenantId as xBlocksKey. In a warm sandbox it is fetched only when you ask (one IAM round trip, once per call), so ask only on the path that calls Blocks. undefined when there is no signed-in caller (a public trigger, a scheduled workflow) or for a client-credentials caller — check it before calling Blocks. Masked in logs. ctx.blocks.accessToken was removed.",
  },
  {
    name: "ctx.run",
    description:
      "id, version, attempt, and invokedBy { type, id } — type is http, workflow, test or replay.",
  },
  {
    name: "ctx.log.info / warn / error",
    description: "Structured lines kept on the run. console.log is captured too.",
  },
  {
    name: "ctx.waitUntil(promise)",
    description:
      "Finish work after the answer is sent, within the time limit — in every mode (reused sandbox, Test run, workflow step).",
  },
] as const;

/**
 * The "Don't / Do instead" list in the Code tab's right rail and the Guide. Each row is a mistake the reuse
 * runtime catches (reuse.mjs: leftover work, late activity, a kept ctx) or one it cannot catch
 * (request data in module variables). Breaking one replaces the warm sandbox after the call —
 * the run shows it as "Sandbox replaced after this call: dirty:…".
 */
export const SANDBOX_CODE_RULES = [
  {
    dont: "Start work and return without waiting for it.",
    dontCode: "saveAudit(entry); // no await",
    fix: "Await it. If it may finish after the answer, pass it to ctx.waitUntil.",
    fixCode: "ctx.waitUntil(saveAudit(entry));",
  },
  {
    dont: "Leave a setTimeout or setInterval running after the handler returns.",
    dontCode: "setInterval(refreshCache, 60_000);",
    fix: "Await a delay inside the handler, or clear the timer before you return. For repeating work, use a scheduled workflow with a Function step.",
    fixCode: "await new Promise((r) => setTimeout(r, 500));",
  },
  {
    dont: "Keep ctx, ctx.log or a token from ctx.blocks.getAccessToken() for later — for example in a client's event listener or a module-level variable.",
    dontCode: 'redis.on("error", (e) => ctx.log.error("redis", e));',
    fix: "Read ctx only inside the handler. In listeners that outlive the call, use console.",
    fixCode: 'redis.on("error", (e) => console.error("redis", e.message));',
  },
  {
    dont: "Store request data in module-level variables — the next caller can see it.",
    dontCode: "let lastUser; // set from input.body",
    fix: "Keep per-call data in local variables. Module level is for clients, constants and caches keyed by tenant.",
    fixCode: "const user = input.body.user; // inside the handler",
  },
  {
    dont: "Connect and close a database client on every call.",
    dontCode: "const db = await MongoClient.connect(url); /* … */ await db.close();",
    fix: "Create the client once at module level and reuse it. Don't close it per call.",
    fixCode: "let client; // module level\nclient ??= await MongoClient.connect(url);",
  },
] as const;

/** Canonical delays behind each backoff choice, so the policy stays one decision in the UI. */
export const BACKOFF_DELAYS = {
  None: { initialDelaySeconds: 0, maxDelaySeconds: 0 },
  Fixed: { initialDelaySeconds: 5, maxDelaySeconds: 5 },
  Exponential: { initialDelaySeconds: 1, maxDelaySeconds: 20 },
} as const;

export const HTTP_METHOD_OPTIONS = ["GET", "POST", "PUT", "PATCH", "DELETE"] as const;

export const FUNCTION_STATUS_LABELS: Record<string, string> = {
  Draft: "Draft",
  Live: "Live",
  Paused: "Paused",
};

export const RUN_STATUS_LABELS: Record<string, string> = {
  Queued: "Queued",
  Claimed: "Claimed",
  Starting: "Starting",
  Running: "Running",
  OutputProcessing: "Output processing",
  Succeeded: "Succeeded",
  Failed: "Failed",
  TimedOut: "Timed out",
  Cancelled: "Cancelled",
  ResourceExceeded: "Resource exceeded",
  OutputFailed: "Output failed",
};
