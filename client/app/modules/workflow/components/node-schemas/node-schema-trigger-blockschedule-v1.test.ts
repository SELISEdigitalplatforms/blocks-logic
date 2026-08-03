import { beforeEach, describe, expect, it } from "vitest";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { NodeSchemaTriggerBlockscheduleV1 as S } from "./node-schema-trigger-blockschedule-v1";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const params = S.schema.parameters as any[];
const p = (key: string) => params.find((f) => f.key === key);

// Every timing field's defaultValue delegates to the same deriveScheduleFields
// helper, so calling one field's defaultValue with a given interval + cron
// exercises the parser branch for that interval.
const derive = (data: Record<string, unknown>) =>
  p("secondsBetweenTriggers").defaultValue(data);

beforeEach(() => {
  (useProjectStore as unknown as { setState: (s: unknown) => void }).setState({
    selectedProject: { tenantId: "t-1", tenantSlug: "s-1", projectKey: "pk" },
  });
});

describe("blockschedule schema shape", () => {
  it("is a v1 blockschedule trigger", () => {
    expect(S.schema.type).toBe("blockschedule");
    expect(S.schema.category).toBe("trigger");
    expect(S.schema.version).toBe("v1");
  });
});

describe("generateCronExpression via onChange", () => {
  const cronFor = (data: Record<string, unknown>) =>
    p("triggerInterval").onChange(undefined, data).cronExpression;

  it("builds a seconds expression", () => {
    expect(cronFor({ triggerInterval: "seconds", secondsBetweenTriggers: 15 }))
      .toBe("*/15 * * * * *");
  });

  it("builds a minutes expression", () => {
    expect(cronFor({ triggerInterval: "minutes", minutesBetweenTriggers: 10 }))
      .toBe("0 */10 * * * *");
  });

  it("builds an hours expression", () => {
    expect(
      cronFor({
        triggerInterval: "hours",
        hoursBetweenTriggers: 2,
        triggerAtMinute: 5,
      }),
    ).toBe("0 5 */2 * * *");
  });

  it("builds a days expression", () => {
    expect(
      cronFor({
        triggerInterval: "days",
        daysBetweenTriggers: 3,
        triggerAtHour: "9",
        triggerAtMinute: 0,
      }),
    ).toBe("0 0 9 */3 * *");
  });

  it("builds a weekly expression for a single week", () => {
    expect(
      cronFor({
        triggerInterval: "weeks",
        weeksBetweenTriggers: 1,
        triggerAtWeek: "2",
        triggerAtHour: "8",
        triggerAtMinute: 30,
      }),
    ).toBe("0 30 8 * * 2");
  });

  it("annotates a multi-week expression", () => {
    expect(
      cronFor({
        triggerInterval: "weeks",
        weeksBetweenTriggers: 3,
        triggerAtWeek: "1",
        triggerAtHour: "8",
        triggerAtMinute: 0,
      }),
    ).toBe("0 0 8 * * 1 # every 3 weeks");
  });

  it("builds a single-month expression", () => {
    expect(
      cronFor({
        triggerInterval: "months",
        monthsBetweenTriggers: 1,
        triggerAtDayOfMonth: 5,
        triggerAtHour: "6",
        triggerAtMinute: 0,
      }),
    ).toBe("0 0 6 5 * *");
  });

  it("builds a multi-month expression", () => {
    expect(
      cronFor({
        triggerInterval: "months",
        monthsBetweenTriggers: 2,
        triggerAtDayOfMonth: 5,
        triggerAtHour: "6",
        triggerAtMinute: 0,
      }),
    ).toBe("0 0 6 5 */2 *");
  });

  it("returns the raw cron for custom mode", () => {
    expect(cronFor({ triggerInterval: "custom", cronExpression: "* * * * *" }))
      .toBe("* * * * *");
  });

  it("clamps out-of-range values", () => {
    expect(cronFor({ triggerInterval: "seconds", secondsBetweenTriggers: 999 }))
      .toBe("*/59 * * * * *");
  });
});

describe("deriveScheduleFields via defaultValue", () => {
  it("returns fallback when the cron is empty", () => {
    expect(derive({ triggerInterval: "seconds", cronExpression: "" })).toBe(30);
  });

  it("returns fallback when the cron has too few tokens", () => {
    expect(derive({ triggerInterval: "seconds", cronExpression: "* *" })).toBe(
      30,
    );
  });

  it("parses seconds step values", () => {
    expect(
      derive({ triggerInterval: "seconds", cronExpression: "*/20 * * * * *" }),
    ).toBe(20);
  });

  it("parses minutes step values", () => {
    expect(
      p("minutesBetweenTriggers").defaultValue({
        triggerInterval: "minutes",
        cronExpression: "0 */12 * * * *",
      }),
    ).toBe(12);
  });

  it("parses hours fields", () => {
    expect(
      p("hoursBetweenTriggers").defaultValue({
        triggerInterval: "hours",
        cronExpression: "0 5 */4 * * *",
      }),
    ).toBe(4);
  });

  it("parses days fields", () => {
    expect(
      p("daysBetweenTriggers").defaultValue({
        triggerInterval: "days",
        cronExpression: "0 0 9 */6 * *",
      }),
    ).toBe(6);
  });

  it("parses weeks fields with a week comment", () => {
    expect(
      p("weeksBetweenTriggers").defaultValue({
        triggerInterval: "weeks",
        cronExpression: "0 0 8 * * 2 # every 4 weeks",
      }),
    ).toBe(4);
  });

  it("parses months fields with a wildcard month token", () => {
    expect(
      p("monthsBetweenTriggers").defaultValue({
        triggerInterval: "months",
        cronExpression: "0 0 6 5 * *",
      }),
    ).toBe(1);
  });

  it("parses months step token", () => {
    expect(
      p("monthsBetweenTriggers").defaultValue({
        triggerInterval: "months",
        cronExpression: "0 0 6 5 */3 *",
      }),
    ).toBe(3);
  });

  it("falls back to default for an unknown interval", () => {
    expect(
      derive({ triggerInterval: "unknown", cronExpression: "*/20 * * * * *" }),
    ).toBe(30);
  });

  it("handles a direct (non-step) numeric token", () => {
    expect(
      derive({ triggerInterval: "seconds", cronExpression: "45 * * * * *" }),
    ).toBe(45);
  });
});

describe("field ui helpers", () => {
  it("cronExpression disabled unless interval is custom", () => {
    expect(p("cronExpression").disabled({ triggerInterval: "days" })).toBe(
      true,
    );
    expect(p("cronExpression").disabled({ triggerInterval: "custom" })).toBe(
      false,
    );
  });

  it("output displayValue mirrors the cron expression", () => {
    expect(p("output").displayValue({ cronExpression: "* * * * *" })).toBe(
      "* * * * *",
    );
    expect(p("output").displayValue({})).toBe("");
  });
});

describe("transform", () => {
  it("generates a cron when none is stored (non-custom)", () => {
    const out = S.transform?.({
      id: "n",
      parameters: {
        triggerInterval: "days",
        daysBetweenTriggers: 2,
        triggerAtHour: "9",
        triggerAtMinute: 0,
      },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.cronExpression).toBe("0 0 9 */2 * *");
    expect(out.parameters.projectKey).toBe("t-1");
  });

  it("keeps a stored cron for custom interval", () => {
    const out = S.transform?.({
      id: "n",
      parameters: { triggerInterval: "custom", cronExpression: "1 2 3 4 5" },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.cronExpression).toBe("1 2 3 4 5");
  });

  it("defaults interval and payload when parameters are missing", () => {
    const out = S.transform?.({
      id: "n",
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.triggerInterval).toBe("days");
    expect(out.parameters.payload).toBe("");
  });
});
