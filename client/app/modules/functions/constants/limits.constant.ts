/** Fallback shown before `GetLimits` resolves — mirrors `FunctionLimits.Ceiling` server-side. */
export const DEFAULT_LIMITS_OPTIONS = {
  ceilingCpuMillicores: 200,
  ceilingMemoryMb: 300,
  ceilingTimeoutSeconds: 60,
  minConcurrency: 1,
  maxConcurrency: 5,
  defaultCpuMillicores: 100,
  defaultMemoryMb: 192,
  defaultTimeoutSeconds: 10,
  defaultConcurrency: 2,
  showRateLimits: false,
} as const;

/**
 * Selectable limit values, from FEATURES-AND-UI §4.7. The design deliberately offers a short list
 * of steps rather than a free number: every option is a value the sandbox actually honours, and the
 * last one in each list is the platform ceiling.
 */
export const MEMORY_MB_OPTIONS = [128, 192, 256, 300] as const;
export const CPU_MILLICORE_OPTIONS = [50, 100, 150, 200] as const;
export const TIMEOUT_SECONDS_OPTIONS = [5, 10, 15, 30, 45, 60] as const;
export const RETRY_ATTEMPTS_OPTIONS = [1, 2, 3, 5] as const;

/** Not configurable — enforced by the runner whatever the caller asks for. */
export const HARD_CAPS = [
  { label: "Input", value: "1 MB" },
  { label: "Result", value: "5 MB" },
  { label: "Logs", value: "1 MB per run" },
  { label: "Temp disk", value: "64 MB" },
  { label: "Processes", value: "64" },
] as const;

/** The "What the sandbox gives you" reference in the Code tab's right rail. */
export const SANDBOX_CTX_DOCS = [
  {
    name: "fetch(url)",
    description:
      "Standard fetch. Public internet is reachable; Blocks internals and private networks are not routed.",
  },
  {
    name: "ctx.env.NAME",
    description: "Your variables, as plain strings. Snapshotted at deploy; secrets are never here.",
  },
  {
    name: "ctx.context",
    description:
      "tenantId, userId, roles, permissions, isAuthenticated. Empty identity on a public call — never a privileged token.",
  },
  { name: "ctx.run", description: "id, version, attempt, invokedBy — http, workflow or test." },
  {
    name: "ctx.log.info / warn / error",
    description: "Structured lines kept on the run. console.log is captured too.",
  },
] as const;

export const BACKOFF_KIND_OPTIONS = [
  { value: "Exponential", label: "Exponential — 1 s, 5 s, 20 s" },
  { value: "Fixed", label: "Fixed — 5 s" },
] as const;

/** Canonical delays behind each backoff choice, so the policy stays one decision in the UI. */
export const BACKOFF_DELAYS = {
  None: { initialDelaySeconds: 0, maxDelaySeconds: 0 },
  Fixed: { initialDelaySeconds: 5, maxDelaySeconds: 5 },
  Exponential: { initialDelaySeconds: 1, maxDelaySeconds: 20 },
} as const;

export const AUTH_MODE_OPTIONS = [
  {
    value: "Token",
    label: "Blocks token",
    hint: "The caller sends a Blocks token. Identity, roles and permissions arrive on ctx.context.",
  },
  {
    value: "Public",
    label: "Public",
    hint: "Anyone with the URL can call it. No identity, no token-scoped work.",
  },
] as const;

export const MATCH_MODE_OPTIONS = [
  { value: "Any", label: "OR" },
  { value: "All", label: "AND" },
] as const;

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
