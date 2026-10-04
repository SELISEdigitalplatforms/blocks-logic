import { describe, expect, it } from "vitest";
import { ProxyResilience } from "../types";
import {
  describeResilience,
  patchResilience,
  PROXY_RESILIENCE_LIMITS,
  resilienceIssues,
  startingRetry,
} from "./proxy-resilience";

const configured: ProxyResilience = {
  timeoutSeconds: 10,
  retry: { attempts: 2, backoff: "exponential", initialDelaySeconds: 1, idempotent: true },
  breaker: { failureThreshold: 5, openSeconds: 30 },
};

describe("patchResilience", () => {
  it("turning the last setting off leaves nothing configured rather than an empty policy", () => {
    // The server stores an all-empty config as no config. A husk kept here would read as
    // "configured" in the console, and on a route it would stop the proxy's policy being inherited.
    const only = { timeoutSeconds: 10, retry: null, breaker: null };

    expect(patchResilience(only, { timeoutSeconds: null })).toBeNull();
  });

  it("keeps the settings it was not asked about", () => {
    expect(patchResilience(configured, { timeoutSeconds: 25 })).toEqual({
      ...configured,
      timeoutSeconds: 25,
    });
  });

  it("builds a policy from nothing when the first setting is turned on", () => {
    expect(patchResilience(null, { breaker: { failureThreshold: 3, openSeconds: 60 } })).toEqual({
      timeoutSeconds: null,
      retry: null,
      breaker: { failureThreshold: 3, openSeconds: 60 },
    });
  });
});

describe("resilienceIssues", () => {
  it("nothing configured is not a problem", () => {
    expect(resilienceIssues(null)).toEqual([]);
    expect(resilienceIssues(configured)).toEqual([]);
  });

  it("refuses retries until the caller says sending the request twice is safe", () => {
    // The rule the setting exists for, and the one the API enforces. Answering it here means the
    // user reads it beside the checkbox instead of as a toast after a failed save.
    const issues = resilienceIssues({ ...configured, retry: startingRetry() });

    expect(issues.map((issue) => issue.path.join("."))).toContain("retry.idempotent");
  });

  it("a single attempt needs no such claim — it is not a retry", () => {
    expect(
      resilienceIssues({
        timeoutSeconds: null,
        breaker: null,
        retry: { attempts: 1, backoff: "none", initialDelaySeconds: 1, idempotent: false },
      }),
    ).toEqual([]);
  });

  it.each([
    ["timeoutSeconds", { ...configured, timeoutSeconds: PROXY_RESILIENCE_LIMITS.maxTimeoutSeconds + 1 }],
    ["timeoutSeconds", { ...configured, timeoutSeconds: 0 }],
    [
      "retry.attempts",
      {
        ...configured,
        retry: { ...configured.retry!, attempts: PROXY_RESILIENCE_LIMITS.maxRetryAttempts + 1 },
      },
    ],
    ["retry.initialDelaySeconds", { ...configured, retry: { ...configured.retry!, initialDelaySeconds: 0 } }],
    ["breaker.failureThreshold", { ...configured, breaker: { failureThreshold: 0, openSeconds: 30 } }],
    [
      "breaker.openSeconds",
      {
        ...configured,
        breaker: { failureThreshold: 5, openSeconds: PROXY_RESILIENCE_LIMITS.maxBreakerOpenSeconds + 1 },
      },
    ],
  ])("reports %s out of the range the API accepts", (path, value) => {
    expect(resilienceIssues(value as ProxyResilience).map((issue) => issue.path.join("."))).toEqual([
      path,
    ]);
  });

  it("a cleared field reads as zero and is reported, not silently accepted", () => {
    // How an emptied number input is stored. It has to produce a message, because the alternative is
    // a save that fails server-side with nothing on screen explaining why.
    expect(resilienceIssues({ ...configured, timeoutSeconds: 0 })).not.toEqual([]);
  });
});

describe("describeResilience", () => {
  it("says so plainly when there is no policy", () => {
    expect(describeResilience(null)).toBe("Not configured");
  });

  it("reads the way the change history reads", () => {
    // The same words in the form and in the row recording that the form changed it.
    expect(describeResilience(configured)).toBe(
      "10s timeout · retries 2 attempts, backing off from 1s · breaker opens after 5 failures, stays open 30s",
    );
  });

  it("names only what was actually set", () => {
    expect(describeResilience({ timeoutSeconds: 5, retry: null, breaker: null })).toBe("5s timeout");
  });
});
