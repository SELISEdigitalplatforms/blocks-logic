import { beforeEach, describe, expect, it } from "vitest";
import { useProjectStore } from "@seliseblocks/genesis-os";
import { NodeSchemaTriggerScheduleV1 as S } from "./node-schema-trigger-schedule-v1";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const params = S.schema.parameters as any[];
const p = (key: string) => params.find((f) => f.key === key);

const derive = (data: Record<string, unknown>) =>
  p("minutesBetweenTriggers").defaultValue(data);

const cronFor = (data: Record<string, unknown>) =>
  p("triggerInterval").onChange(undefined, data, {
    projectKey: "pk",
    workflowId: "wf-1",
    nodeId: "node-1",
  }).cronExpression;

beforeEach(() => {
  (useProjectStore as unknown as { setState: (s: unknown) => void }).setState({
    selectedProject: { tenantId: "t-1", tenantSlug: "s-1", projectKey: "pk" },
  });
});

describe("schedule schema shape", () => {
  it("is a v1 schedule trigger", () => {
    expect(S.schema.type).toBe("schedule");
    expect(S.schema.category).toBe("trigger");
    expect(S.schema.version).toBe("v1");
  });

  it("does not offer a seconds interval (Hangfire is minute-based)", () => {
    const options = p("triggerInterval").options as { value: string }[];
    expect(options.map((o) => o.value)).not.toContain("seconds");
  });
});

describe("generateCronExpression via onChange", () => {
  it("builds a minutes expression", () => {
    expect(cronFor({ triggerInterval: "minutes", minutesBetweenTriggers: 10 }))
      .toBe("*/10 * * * *");
  });

  it("collapses the seconds interval to every minute (5-field cron minimum)", () => {
    expect(cronFor({ triggerInterval: "seconds", secondsBetweenTriggers: 15 }))
      .toBe("* * * * *");
  });

  it("always emits 5-field expressions", () => {
    const expressions = [
      cronFor({ triggerInterval: "minutes", minutesBetweenTriggers: 5 }),
      cronFor({ triggerInterval: "hours", hoursBetweenTriggers: 2, triggerAtMinute: 5 }),
      cronFor({ triggerInterval: "days", triggerAtHour: "9", triggerAtMinute: 0 }),
      cronFor({ triggerInterval: "weeks", triggerAtWeekdays: ["1", "3"] }),
      cronFor({ triggerInterval: "months", monthsBetweenTriggers: 3 }),
    ];
    for (const expression of expressions) {
      expect(expression.split(" ")).toHaveLength(5);
    }
  });

  it("builds an hours expression with minute", () => {
    expect(cronFor({
      triggerInterval: "hours",
      hoursBetweenTriggers: 2,
      triggerAtMinute: 5,
    })).toBe("5 */2 * * *");
  });

  it("builds a days expression (daily at time)", () => {
    expect(cronFor({
      triggerInterval: "days",
      triggerAtHour: "9",
      triggerAtMinute: 0,
    })).toBe("0 9 * * *");
  });

  it("builds a weeks expression with multiple weekdays", () => {
    expect(cronFor({
      triggerInterval: "weeks",
      triggerAtWeekdays: ["3", "1"],
      triggerAtHour: "8",
      triggerAtMinute: 30,
    })).toBe("30 8 * * 1,3");
  });

  it("uses * when no weekday is selected", () => {
    expect(cronFor({
      triggerInterval: "weeks",
      triggerAtWeekdays: [],
      triggerAtHour: "8",
      triggerAtMinute: 0,
    })).toBe("0 8 * * *");
  });

  it("builds a months expression for a dividing interval", () => {
    expect(cronFor({
      triggerInterval: "months",
      monthsBetweenTriggers: 3,
      triggerAtDayOfMonth: 5,
      triggerAtHour: "6",
      triggerAtMinute: 0,
    })).toBe("0 6 5 */3 *");
  });

  it("falls back to every month for a non-dividing interval", () => {
    expect(cronFor({
      triggerInterval: "months",
      monthsBetweenTriggers: 5,
      triggerAtDayOfMonth: 5,
      triggerAtHour: "6",
      triggerAtMinute: 0,
    })).toBe("0 6 5 * *");
  });

  it("caps day of month at 28", () => {
    expect(cronFor({
      triggerInterval: "months",
      monthsBetweenTriggers: 1,
      triggerAtDayOfMonth: 31,
      triggerAtHour: "6",
      triggerAtMinute: 0,
    })).toBe("0 6 28 */1 *");
  });

  it("returns the raw cron for custom mode", () => {
    expect(cronFor({ triggerInterval: "custom", cronExpression: "*/10 * * * *" }))
      .toBe("*/10 * * * *");
  });

  it("clamps out-of-range values", () => {
    expect(cronFor({ triggerInterval: "minutes", minutesBetweenTriggers: 999 }))
      .toBe("*/59 * * * *");
  });
});

describe("deriveScheduleFields via defaultValue", () => {
  it("returns fallback when the cron is empty", () => {
    expect(derive({ triggerInterval: "minutes", cronExpression: "" })).toBe(5);
  });

  it("returns fallback when the cron has too few tokens", () => {
    expect(derive({ triggerInterval: "minutes", cronExpression: "* *" })).toBe(5);
  });

  it("parses minutes step values from a 5-field cron", () => {
    expect(
      p("minutesBetweenTriggers").defaultValue({
        triggerInterval: "minutes",
        cronExpression: "*/12 * * * *",
      }),
    ).toBe(12);
  });

  it("parses minutes step values from a legacy 6-field cron", () => {
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
        cronExpression: "5 */4 * * *",
      }),
    ).toBe(4);
  });

  it("parses hours fields from a legacy 6-field cron", () => {
    expect(
      p("hoursBetweenTriggers").defaultValue({
        triggerInterval: "hours",
        cronExpression: "0 5 */4 * * *",
      }),
    ).toBe(4);
  });

  it("parses weeks fields into a weekday list", () => {
    expect(
      p("triggerAtWeekdays").defaultValue({
        triggerInterval: "weeks",
        cronExpression: "0 8 * * 1,3,5",
      }),
    ).toEqual(["1", "3", "5"]);
  });

  it("defaults weekday list when the token is a wildcard", () => {
    expect(
      p("triggerAtWeekdays").defaultValue({
        triggerInterval: "weeks",
        cronExpression: "0 8 * * *",
      }),
    ).toEqual(["1"]);
  });

  it("parses months fields with a wildcard month token", () => {
    expect(
      p("monthsBetweenTriggers").defaultValue({
        triggerInterval: "months",
        cronExpression: "0 6 5 * *",
      }),
    ).toBe(1);
  });

  it("parses months step token", () => {
    expect(
      p("monthsBetweenTriggers").defaultValue({
        triggerInterval: "months",
        cronExpression: "0 6 5 */3 *",
      }),
    ).toBe(3);
  });

  it("falls back to default for an unknown interval", () => {
    expect(
      derive({ triggerInterval: "unknown", cronExpression: "*/20 * * * *" }),
    ).toBe(5);
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
    expect(p("output").displayValue({ cronExpression: "*/10 * * * *" })).toBe(
      "*/10 * * * *",
    );
    expect(p("output").displayValue({})).toBe("");
  });

  it("does not carry start or end dates", () => {
    expect(p("startDate")).toBeUndefined();
    expect(p("endDate")).toBeUndefined();
    expect(S.defaults.parameters).not.toHaveProperty("startDate");
    expect(S.defaults.parameters).not.toHaveProperty("endDate");
  });
});


describe("onChange preserves sibling fields (transient regression)", () => {
  // Simulates persisted node state: transient sub-fields were stripped,
  // only triggerInterval + cronExpression survive.
  // The form builder sets the new value into `data` before invoking onChange.
  const hourChange = p("triggerAtHour").onChange("14", {
    triggerInterval: "days",
    cronExpression: "30 9 * * *",
    triggerAtHour: "14",
  });

  it("keeps the stored minute when the hour changes", () => {
    expect(hourChange.cronExpression).toBe("30 14 * * *");
  });

  it("keeps stored weekdays when the hour changes in weeks mode", () => {
    const change = p("triggerAtHour").onChange("8", {
      triggerInterval: "weeks",
      cronExpression: "30 9 * * 1,3",
      triggerAtHour: "8",
    });
    expect(change.cronExpression).toBe("30 8 * * 1,3");
  });

  it("keeps stored day of month and hour when the minute changes in months mode", () => {
    const change = p("triggerAtMinute").onChange(15, {
      triggerInterval: "months",
      cronExpression: "0 6 5 */3 *",
      triggerAtMinute: 15,
    });
    expect(change.cronExpression).toBe("15 6 5 */3 *");
  });

  it("keeps the stored minute step when the interval changes to minutes", () => {
    const change = p("triggerInterval").onChange(undefined, {
      triggerInterval: "minutes",
      cronExpression: "*/12 * * * *",
    });
    expect(change.cronExpression).toBe("*/12 * * * *");
  });
});

describe("transform", () => {
  it("generates a cron when none is stored (non-custom)", () => {
    const out = S.transform?.({
      id: "n",
      parameters: {
        triggerInterval: "days",
        triggerAtHour: "9",
        triggerAtMinute: 0,
      },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.cronExpression).toBe("0 9 * * *");
  });

  it("keeps a stored cron for custom interval", () => {
    const out = S.transform?.({
      id: "n",
      parameters: { triggerInterval: "custom", cronExpression: "1 2 3 4 5" },
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.cronExpression).toBe("1 2 3 4 5");
  });

  it("defaults interval when parameters are missing", () => {
    const out = S.transform?.({
      id: "n",
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
    } as any) as { parameters: Record<string, unknown> };
    expect(out.parameters.triggerInterval).toBe("days");
  });
});
