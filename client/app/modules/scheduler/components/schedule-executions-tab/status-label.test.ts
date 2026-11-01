import { describe, expect, it } from "vitest";
import {
  formatDuration,
  isSchedulerRecorded,
  isTruncated,
  sheetStatusLabel,
  statusLabel,
} from "./status-label";

describe("statusLabel", () => {
  it("is HTTP {status}, or a dash without one", () => {
    expect(statusLabel(200)).toBe("HTTP 200");
    expect(statusLabel(502)).toBe("HTTP 502");
    expect(statusLabel(null)).toBe("—");
    expect(statusLabel(undefined)).toBe("—");
  });

  it("reads Queued only for an accepted queue send", () => {
    expect(sheetStatusLabel(202, 2)).toBe("Queued");
    expect(sheetStatusLabel(202, 1)).toBe("HTTP 202");
    expect(sheetStatusLabel(503, 2)).toBe("HTTP 503");
    expect(sheetStatusLabel(null, null)).toBe("—");
  });
});

describe("body helpers", () => {
  it("recognises the scheduler's own bodies", () => {
    expect(isSchedulerRecorded("Network error: no such host")).toBe(true);
    expect(isSchedulerRecorded("Webhook is not configured.")).toBe(true);
    expect(isSchedulerRecorded("Queue send failed: down")).toBe(true);
    expect(isSchedulerRecorded('{"error":"Network error: x"}')).toBe(false);
    expect(isSchedulerRecorded(null)).toBe(false);
  });

  it("recognises a truncated body", () => {
    expect(isTruncated("aaa\n…[truncated]")).toBe(true);
    expect(isTruncated("aaa")).toBe(false);
    expect(isTruncated(undefined)).toBe(false);
  });
});

describe("formatDuration", () => {
  it("uses ms below a second and s from 1,000 ms", () => {
    expect(formatDuration(340)).toBe("340 ms");
    expect(formatDuration(999.6)).toBe("1000 ms");
    expect(formatDuration(1000)).toBe("1.0 s");
    expect(formatDuration(12_340)).toBe("12.3 s");
    expect(formatDuration(null)).toBe("—");
    expect(formatDuration(Number.NaN)).toBe("—");
  });
});
