import { describe, expect, it } from "vitest";
import { parseSnapshot } from "./parse-snapshot";

const webhookSnapshot = JSON.stringify({
  itemId: "s1",
  name: "Nightly sync",
  description: "desc",
  payload: '{"a":1}',
  cronExpression: "*/5 * * * *",
  timeZone: "Asia/Dhaka",
  startDate: "2026-10-01T00:00:00Z",
  endDate: null,
  triggerType: 1,
  webhook: {
    url: "https://hooks.example.com/x?k=1",
    method: "POST",
    headers: { Authorization: "[redacted]", "X-Token": "[redacted]" },
    signingSecret: "[redacted]",
  },
  queue: null,
});

describe("parseSnapshot", () => {
  it("is none for a null or empty snapshot", () => {
    expect(parseSnapshot(null)).toEqual({ kind: "none" });
    expect(parseSnapshot("")).toEqual({ kind: "none" });
  });

  it("is invalid for text that is not a JSON object", () => {
    expect(parseSnapshot("{oops")).toEqual({ kind: "invalid", raw: "{oops" });
    expect(parseSnapshot("[1,2]")).toEqual({ kind: "invalid", raw: "[1,2]" });
    expect(parseSnapshot("null")).toEqual({ kind: "invalid", raw: "null" });
  });

  it("reads a webhook snapshot", () => {
    const parsed = parseSnapshot(webhookSnapshot);
    expect(parsed.kind).toBe("ok");
    if (parsed.kind !== "ok") return;
    expect(parsed.pretty).toContain('\n  "itemId": "s1"');
    expect(parsed.snapshot).toEqual({
      name: "Nightly sync",
      description: "desc",
      payload: '{"a":1}',
      cronExpression: "*/5 * * * *",
      timeZone: "Asia/Dhaka",
      startDate: "2026-10-01T00:00:00Z",
      endDate: null,
      triggerType: 1,
      webhook: {
        method: "POST",
        url: "https://hooks.example.com/x?k=1",
        headerNames: ["Authorization", "X-Token"],
        signed: true,
      },
      queueName: null,
    });
  });

  it("reads a queue snapshot and tolerates missing or mistyped keys", () => {
    const parsed = parseSnapshot(JSON.stringify({ triggerType: "2", queue: { queueName: "q1" }, webhook: { headers: [] } }));
    if (parsed.kind !== "ok") throw new Error("expected ok");
    expect(parsed.snapshot.queueName).toBe("q1");
    expect(parsed.snapshot.triggerType).toBeNull();
    expect(parsed.snapshot.cronExpression).toBeNull();
    expect(parsed.snapshot.webhook).toEqual({ method: null, url: null, headerNames: [], signed: false });
  });
});
