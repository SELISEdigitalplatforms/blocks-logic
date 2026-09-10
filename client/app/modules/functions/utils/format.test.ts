import { describe, expect, it } from "vitest";
import {
  formatDuration,
  formatMemoryAgainstLimit,
  formatRelativeTime,
  formatRunCount,
  formatTimeOfDay,
} from "./format";

const NOW = new Date("2026-09-10T12:00:00.000Z").getTime();
const ago = (ms: number) => new Date(NOW - ms).toISOString();

describe("formatRelativeTime", () => {
  it("reads as the design's relative times", () => {
    expect(formatRelativeTime(ago(20_000), NOW)).toBe("just now");
    expect(formatRelativeTime(ago(3 * 60_000), NOW)).toBe("3 min ago");
    expect(formatRelativeTime(ago(60 * 60_000), NOW)).toBe("1 hour ago");
    expect(formatRelativeTime(ago(5 * 60 * 60_000), NOW)).toBe("5 hours ago");
    expect(formatRelativeTime(ago(24 * 60 * 60_000), NOW)).toBe("yesterday");
    expect(formatRelativeTime(ago(4 * 24 * 60 * 60_000), NOW)).toBe("4 days ago");
  });

  it("has an em dash for a function that has never run", () => {
    expect(formatRelativeTime(null, NOW)).toBe("—");
  });

  it("passes an unparseable value straight through rather than showing NaN", () => {
    expect(formatRelativeTime("not-a-date", NOW)).toBe("not-a-date");
  });
});

describe("formatDuration", () => {
  it("switches unit with magnitude", () => {
    expect(formatDuration(684)).toBe("684 ms");
    expect(formatDuration(2_040)).toBe("2.04 s");
    expect(formatDuration(75_000)).toBe("1 min 15 s");
    expect(formatDuration(null)).toBe("—");
  });
});

describe("formatMemoryAgainstLimit", () => {
  it("shows peak memory against the limit the run had", () => {
    expect(formatMemoryAgainstLimit(86_000_000, 192)).toBe("82 / 192 MB");
  });

  it("drops the limit when it is unknown, and the whole thing when nothing was measured", () => {
    expect(formatMemoryAgainstLimit(86_000_000, null)).toBe("82 MB");
    expect(formatMemoryAgainstLimit(null, 192)).toBe("—");
  });
});

describe("formatRunCount", () => {
  it("groups thousands, as the list does", () => {
    expect(formatRunCount(1204)).toBe("1,204");
    expect(formatRunCount(0)).toBe("0");
  });
});

describe("formatTimeOfDay", () => {
  it("keeps milliseconds, which log lines and the stage timeline need", () => {
    expect(formatTimeOfDay("2026-09-10T12:00:00.118Z")).toMatch(/\.118$/);
  });
});
