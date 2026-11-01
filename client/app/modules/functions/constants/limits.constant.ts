import { tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_MODULE = "logic.functions";
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
  { get label() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.INPUT"); }, value: "Size-limited; a larger request is refused" },
  { get label() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.RESULT"); }, value: "Size-limited; return a reference for large data" },
  { get label() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.LOGS"); }, value: "Capped per run; lines past the cap are dropped" },
  { get label() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.TEMP_DISK"); }, value: "Small /tmp only; files can't be executed" },
  { get label() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.PROCESSES"); }, value: "Capped per sandbox" },
] as const;

/** The "What the sandbox gives you" reference in the Code tab's right rail. */
export const SANDBOX_CTX_DOCS = [
  {
    name: "input",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.THE_REQUEST_METHOD_PATH"); },
  },
  {
    name: "fetch(url)",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.STANDARD_FETCH_PUBLIC_INTERNET"); },
  },
  {
    name: "ctx.env.NAME",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.YOUR_VARIABLES_AS_PLAIN"); },
  },
  {
    name: "ctx.context",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.TENANTID_USERID_ROLES_PERMISSIONS"); },
  },
  {
    name: "await ctx.blocks.getAccessToken()",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.THE_CALLER_S_BLOCKS"); },
  },
  {
    name: "ctx.run",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.ID_VERSION_ATTEMPT_AND"); },
  },
  {
    name: "ctx.log.info / warn / error",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.STRUCTURED_LINES_KEPT_ON"); },
  },
  {
    name: "ctx.waitUntil(promise)",
    get description() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.FINISH_WORK_AFTER_THE"); },
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
    get dont() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.START_WORK_AND_RETURN"); },
    dontCode: "saveAudit(entry); // no await",
    get fix() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.AWAIT_IT_IF_IT"); },
    fixCode: "ctx.waitUntil(saveAudit(entry));",
  },
  {
    get dont() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.LEAVE_A_SETTIMEOUT_OR"); },
    dontCode: "setInterval(refreshCache, 60_000);",
    get fix() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.AWAIT_A_DELAY_INSIDE"); },
    fixCode: "await new Promise((r) => setTimeout(r, 500));",
  },
  {
    get dont() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.KEEP_CTX_CTX_LOG"); },
    dontCode: 'redis.on("error", (e) => ctx.log.error("redis", e));',
    get fix() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.READ_CTX_ONLY_INSIDE"); },
    fixCode: 'redis.on("error", (e) => console.error("redis", e.message));',
  },
  {
    get dont() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.STORE_REQUEST_DATA_IN"); },
    dontCode: "let lastUser; // set from input.body",
    get fix() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.KEEP_PER_CALL_DATA"); },
    fixCode: "const user = input.body.user; // inside the handler",
  },
  {
    get dont() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.CONNECT_AND_CLOSE_A"); },
    dontCode: "const db = await MongoClient.connect(url); /* … */ await db.close();",
    get fix() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.CREATE_THE_CLIENT_ONCE"); },
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
  get Draft() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.DRAFT"); },
  get Live() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.LIVE"); },
  get Paused() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.PAUSED"); },
};

export const RUN_STATUS_LABELS: Record<string, string> = {
  get Queued() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.QUEUED"); },
  get Claimed() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.CLAIMED"); },
  get Starting() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.STARTING"); },
  get Running() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.RUNNING"); },
  get OutputProcessing() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.OUTPUT_PROCESSING"); },
  get Succeeded() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.SUCCEEDED"); },
  get Failed() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.FAILED"); },
  get TimedOut() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.TIMED_OUT"); },
  get Cancelled() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.CANCELLED"); },
  get ResourceExceeded() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.RESOURCE_EXCEEDED"); },
  get OutputFailed() { return tStatic(LOGIC_FUNCTIONS_MODULE, "LIMITS_CONSTANT.OUTPUT_FAILED"); },
};
