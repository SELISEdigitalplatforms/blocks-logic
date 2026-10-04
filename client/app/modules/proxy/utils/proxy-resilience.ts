import { ProxyBreaker, ProxyResilience, ProxyRetry } from "../types";

/**
 * The server's own bounds, from `ProxyConfigValidator`. Mirrored here rather than guessed: a form that
 * accepts a value the API refuses turns an edit the user can fix into a save that fails.
 */
export const PROXY_RESILIENCE_LIMITS = {
  maxTimeoutSeconds: 30,
  /** Total attempts including the first; 1 is "no retry" and is stored as not configured. */
  maxRetryAttempts: 3,
  maxRetryDelaySeconds: 30,
  maxBreakerThreshold: 100,
  maxBreakerOpenSeconds: 600,
} as const;

/**
 * What a call gets when no timeout is configured — the gateway's own client limit. Shown so "not
 * configured" is a knowable state rather than a blank, and never written into the form.
 */
export const PROXY_PLATFORM_TIMEOUT_SECONDS = 30;

/**
 * The values a panel starts from the moment the user switches one of these on.
 *
 * They are not defaults: nothing here reaches a proxy until somebody flips the switch, and once
 * flipped the numbers are on screen and editable. {@link ProxyRetry.idempotent} deliberately starts
 * false — the server refuses retries without it, so the claim has to be made rather than inherited.
 */
export const startingTimeoutSeconds = 10;

export const startingRetry = (): ProxyRetry => ({
  attempts: 2,
  backoff: "exponential",
  initialDelaySeconds: 1,
  idempotent: false,
});

export const startingBreaker = (): ProxyBreaker => ({ failureThreshold: 5, openSeconds: 30 });

/**
 * Applies one change to a resilience object, collapsing to `null` when nothing is left asked for.
 *
 * The collapse matters: the server stores an all-empty config as no config, so a form that kept an
 * `{ timeout: null, retry: null, breaker: null }` husk would show "configured" for a policy that does
 * nothing, and a route holding one would stop inheriting the connection's.
 */
export const patchResilience = (
  current: ProxyResilience | null | undefined,
  changes: Partial<ProxyResilience>,
): ProxyResilience | null => {
  const next: ProxyResilience = {
    timeoutSeconds: current?.timeoutSeconds ?? null,
    retry: current?.retry ?? null,
    breaker: current?.breaker ?? null,
    ...changes,
  };

  return next.timeoutSeconds === null && next.retry === null && next.breaker === null ? null : next;
};

/** A problem with one field of a resilience object, as a path relative to that object. */
export type ResilienceIssue = { path: Array<string | number>; message: string };

const outOfRange = (value: number, max: number) => !Number.isInteger(value) || value < 1 || value > max;

/**
 * Every reason the server would refuse this object, checked here so it is answered beside the field
 * instead of as a toast after a round trip. Wording follows the API's, so the two never disagree about
 * what the rule is.
 */
export const resilienceIssues = (value: ProxyResilience | null | undefined): ResilienceIssue[] => {
  if (!value) return [];

  const issues: ResilienceIssue[] = [];
  const limits = PROXY_RESILIENCE_LIMITS;

  if (value.timeoutSeconds !== null && outOfRange(value.timeoutSeconds, limits.maxTimeoutSeconds)) {
    issues.push({
      path: ["timeoutSeconds"],
      message: `The timeout must be between 1 and ${limits.maxTimeoutSeconds} seconds.`,
    });
  }

  if (value.retry) {
    const { attempts, initialDelaySeconds, idempotent } = value.retry;

    if (outOfRange(attempts, limits.maxRetryAttempts)) {
      issues.push({
        path: ["retry", "attempts"],
        message: `Attempts must be between 1 and ${limits.maxRetryAttempts}.`,
      });
    }

    // The rule the whole setting exists for. Blocks cannot tell whether an upstream tolerates the same
    // request twice, so more than one attempt needs the tenant to say so.
    if (attempts > 1 && !idempotent) {
      issues.push({
        path: ["retry", "idempotent"],
        message:
          "Confirm that sending this request more than once is safe before retries can be saved.",
      });
    }

    if (outOfRange(initialDelaySeconds, limits.maxRetryDelaySeconds)) {
      issues.push({
        path: ["retry", "initialDelaySeconds"],
        message: `The delay must be between 1 and ${limits.maxRetryDelaySeconds} seconds.`,
      });
    }
  }

  if (value.breaker) {
    if (outOfRange(value.breaker.failureThreshold, limits.maxBreakerThreshold)) {
      issues.push({
        path: ["breaker", "failureThreshold"],
        message: `The failure count must be between 1 and ${limits.maxBreakerThreshold}.`,
      });
    }

    if (outOfRange(value.breaker.openSeconds, limits.maxBreakerOpenSeconds)) {
      issues.push({
        path: ["breaker", "openSeconds"],
        message: `The pause must be between 1 and ${limits.maxBreakerOpenSeconds} seconds.`,
      });
    }
  }

  return issues;
};

// ---------------------------------------------------------------------------
// Words
// ---------------------------------------------------------------------------
// The same phrasing the change history uses (ProxyChangeSet), so a setting reads the same way in the
// form that edits it and in the row recording that it changed.

export const describeTimeout = (seconds: number | null) => (seconds === null ? "not set" : `${seconds}s`);

export const describeRetry = (retry: ProxyRetry | null) => {
  if (!retry) return "off";

  const shape =
    retry.backoff === "fixed"
      ? `, every ${retry.initialDelaySeconds}s`
      : retry.backoff === "exponential"
        ? `, backing off from ${retry.initialDelaySeconds}s`
        : ", immediately";

  return `${retry.attempts} attempts${shape}`;
};

export const describeBreaker = (breaker: ProxyBreaker | null) =>
  breaker
    ? `opens after ${breaker.failureThreshold} failures, stays open ${breaker.openSeconds}s`
    : "off";

/** One line for a summary row: what this policy does, or that there isn't one. */
export const describeResilience = (value: ProxyResilience | null | undefined): string => {
  if (!value) return "Not configured";

  const parts = [
    value.timeoutSeconds === null ? null : `${value.timeoutSeconds}s timeout`,
    value.retry ? `retries ${describeRetry(value.retry)}` : null,
    value.breaker ? `breaker ${describeBreaker(value.breaker)}` : null,
  ].filter((part): part is string => part !== null);

  return parts.length ? parts.join(" · ") : "Not configured";
};
