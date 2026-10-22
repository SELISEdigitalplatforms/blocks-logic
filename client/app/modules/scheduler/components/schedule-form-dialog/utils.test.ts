import { describe, expect, it } from "vitest";
import {
  CRON_PRESETS,
  scheduleFormDefaultValues,
  scheduleFormSchema,
  ScheduleFormValues,
  toIsoOrNull,
  webhookHeadersToArray,
  webhookHeadersToRecord,
} from "./utils";

const validValues: ScheduleFormValues = {
  ...scheduleFormDefaultValues,
  name: "Nightly sync",
  webhook: { ...scheduleFormDefaultValues.webhook, url: "https://example.com/hook" },
  cronExpression: "0 9 * * *",
};

const issuesFor = (values: Partial<ScheduleFormValues>) => {
  const result = scheduleFormSchema.safeParse({ ...validValues, ...values });
  return result.success ? [] : result.error.issues;
};

describe("scheduleFormSchema", () => {
  it("accepts a complete form", () => {
    expect(issuesFor({})).toEqual([]);
  });

  it.each(CRON_PRESETS.map((preset) => [preset.label, preset.value]))(
    "accepts the %s preset",
    (_label, cronExpression) => {
      expect(issuesFor({ cronExpression })).toEqual([]);
    },
  );

  it("requires a cron expression and reports it once", () => {
    const issues = issuesFor({ cronExpression: "" });
    expect(issues).toHaveLength(1);
    expect(issues[0]).toMatchObject({
      path: ["cronExpression"],
      message: "Cron expression is required",
    });
  });

  it("rejects an out-of-range cron value with the field's message", () => {
    expect(issuesFor({ cronExpression: "0 25 * * *" })).toEqual([
      expect.objectContaining({ path: ["cronExpression"], message: "Hour must be 0-23" }),
    ]);
  });

  it("rejects invalid payload JSON and accepts an empty payload", () => {
    expect(issuesFor({ payload: "{ nope" })[0]?.message).toBe("Payload must be valid JSON");
    expect(issuesFor({ payload: "  " })).toEqual([]);
    expect(issuesFor({ payload: '{"a":1}' })).toEqual([]);
  });

  it("rejects an end date before the start date", () => {
    expect(issuesFor({ startDate: "2026-10-10", endDate: "2026-10-01" })).toEqual([
      expect.objectContaining({ path: ["endDate"], message: "End date must be after start date" }),
    ]);
  });

  it("requires a header value when its key is set", () => {
    const issues = issuesFor({
      webhook: { ...validValues.webhook, headers: [{ key: "X-Trace", value: " " }] },
    });
    expect(issues).toEqual([
      expect.objectContaining({ path: ["webhook", "headers", 0, "value"] }),
    ]);
  });
});

describe("toIsoOrNull", () => {
  it("returns null for an empty value and an ISO string otherwise", () => {
    expect(toIsoOrNull("")).toBeNull();
    expect(toIsoOrNull(undefined)).toBeNull();
    expect(toIsoOrNull("2026-10-01T00:00:00Z")).toBe("2026-10-01T00:00:00.000Z");
  });
});

describe("webhook header helpers", () => {
  it("converts a header record to rows and handles a missing record", () => {
    expect(webhookHeadersToArray({ A: "1" })).toEqual([{ key: "A", value: "1" }]);
    expect(webhookHeadersToArray(null)).toEqual([]);
  });

  it("converts rows to a record, trimming keys and dropping blank ones", () => {
    expect(
      webhookHeadersToRecord([
        { key: " A ", value: "1" },
        { key: " ", value: "ignored" },
      ]),
    ).toEqual({ A: "1" });
    expect(webhookHeadersToRecord([{ key: "", value: "" }])).toBeNull();
  });
});
