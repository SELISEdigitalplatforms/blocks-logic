import { describe, expect, it } from "vitest";
import { RUN_POLL_CUTOFF_MS, runPollInterval } from "./run-polling";

const NOW = Date.parse("2026-09-29T12:00:00Z");
const ago = (ms: number) => new Date(NOW - ms).toISOString();

describe("runPollInterval", () => {
  it("polls at the base rate for a fresh run", () => {
    expect(runPollInterval(ago(30_000), 2000, NOW)).toBe(2000);
  });

  it("backs off to 15 s, then a minute, as the run ages", () => {
    expect(runPollInterval(ago(5 * 60_000), 2000, NOW)).toBe(15_000);
    expect(runPollInterval(ago(60 * 60_000), 2000, NOW)).toBe(60_000);
  });

  it("never polls faster than the caller's base", () => {
    expect(runPollInterval(ago(5 * 60_000), 30_000, NOW)).toBe(30_000);
  });

  it("stops once the run is past the server's run TTL", () => {
    expect(runPollInterval(ago(RUN_POLL_CUTOFF_MS), 2000, NOW)).toBe(false);
    expect(runPollInterval(ago(RUN_POLL_CUTOFF_MS + 1), 2000, NOW)).toBe(false);
  });

  it.each([undefined, null, "", "not a date"])("keeps polling at the base rate for %j", (value) => {
    expect(runPollInterval(value, 2000, NOW)).toBe(2000);
  });

  it("keeps polling at the base rate for a created date in the future (clock skew)", () => {
    expect(runPollInterval(new Date(NOW + 60_000).toISOString(), 2000, NOW)).toBe(2000);
  });
});
