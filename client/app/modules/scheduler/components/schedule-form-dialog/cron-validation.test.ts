import { describe, expect, it } from "vitest";
import { validateCronExpression } from "./cron-validation";
import { CRON_PRESETS } from "./utils";

describe("validateCronExpression", () => {
  it.each(CRON_PRESETS.map((preset) => [preset.label, preset.value]))(
    "accepts the %s preset (%s)",
    (_label, value) => {
      expect(validateCronExpression(value)).toBeNull();
    },
  );

  it.each([
    "0 0 * * *",
    "59 23 31 12 7",
    "0 0 1 1 0",
    "*/15 * * * *",
    "0-30/5 9-17 * * 1-5",
    "5/15 * * * *",
    "0,15,30,45 * * * *",
    "0 9 ? * MON",
    "0 9 15 * ?",
    "0 9 * JAN-MAR SUN,SAT",
    "0 9 * DEC *",
    "0 9 * * 1-FRI",
    "  0 9 * * *  ",
    "0  9 *  * *",
  ])("accepts %s", (expression) => {
    expect(validateCronExpression(expression)).toBeNull();
  });

  it.each([
    ["", "Cron expression must have 5 fields"],
    ["* * * *", "Cron expression must have 5 fields"],
    ["0 * * * * *", "Cron expression must have 5 fields"],
    ["60 * * * *", "Minute must be 0-59"],
    ["99 99 99 99 99", "Minute must be 0-59"],
    ["0 24 * * *", "Hour must be 0-23"],
    ["0 0 0 * *", "Day of month must be 1-31"],
    ["0 0 32 * *", "Day of month must be 1-31"],
    ["0 0 * 0 *", "Month must be 1-12 or JAN-DEC"],
    ["0 0 * 13 *", "Month must be 1-12 or JAN-DEC"],
    ["0 0 * FOO *", "Month must be 1-12 or JAN-DEC"],
    ["0 0 * * 8", "Day of week must be 0-7 or SUN-SAT"],
    ["0 0 * * mon", "Day of week must be 0-7 or SUN-SAT"],
    ["0 0 * * JAN", "Day of week must be 0-7 or SUN-SAT"],
    ["5-1 * * * *", "Minute must be 0-59"],
    ["0 9 * * FRI-MON", "Day of week must be 0-7 or SUN-SAT"],
    ["*/0 * * * *", "Minute must be 0-59"],
    ["*/60 * * * *", "Minute must be 0-59"],
    ["*/x * * * *", "Minute must be 0-59"],
    ["1/2/3 * * * *", "Minute must be 0-59"],
    ["1-2-3 * * * *", "Minute must be 0-59"],
    ["1,,2 * * * *", "Minute must be 0-59"],
    ["L * * * *", "Minute must be 0-59"],
    ["0 9 L * *", "Day of month must be 1-31"],
    ["0 9 * * 5#2", "Day of week must be 0-7 or SUN-SAT"],
    ["? * * * *", 'Minute does not allow "?"'],
    ["0 * * ? *", 'Month does not allow "?"'],
    ["0 9 ? * ?", 'Day of month and day of week cannot both be "?"'],
  ])("rejects %s", (expression, message) => {
    expect(validateCronExpression(expression)).toContain(message);
  });
});
