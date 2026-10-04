import { NodeGuideTriggerScheduleV1 } from "../node-guides";
import { NodeSchemaDefinition } from "./node-schema.type";

const TRIGGER_INTERVAL_OPTIONS = [
  { label: "Minutes", value: "minutes" },
  { label: "Hours", value: "hours" },
  { label: "Days", value: "days" },
  { label: "Weeks", value: "weeks" },
  { label: "Custom (Cron)", value: "custom" },
];

const HOUR_OPTIONS = [
  { label: "12am", value: "0" },
  { label: "1am", value: "1" },
  { label: "2am", value: "2" },
  { label: "3am", value: "3" },
  { label: "4am", value: "4" },
  { label: "5am", value: "5" },
  { label: "6am", value: "6" },
  { label: "7am", value: "7" },
  { label: "8am", value: "8" },
  { label: "9am", value: "9" },
  { label: "10am", value: "10" },
  { label: "11am", value: "11" },
  { label: "12pm", value: "12" },
  { label: "1pm", value: "13" },
  { label: "2pm", value: "14" },
  { label: "3pm", value: "15" },
  { label: "4pm", value: "16" },
  { label: "5pm", value: "17" },
  { label: "6pm", value: "18" },
  { label: "7pm", value: "19" },
  { label: "8pm", value: "20" },
  { label: "9pm", value: "21" },
  { label: "10pm", value: "22" },
  { label: "11pm", value: "23" },
];

const WEEKDAY_OPTIONS = [
  { label: "Sunday", value: "0" },
  { label: "Monday", value: "1" },
  { label: "Tuesday", value: "2" },
  { label: "Wednesday", value: "3" },
  { label: "Thursday", value: "4" },
  { label: "Friday", value: "5" },
  { label: "Saturday", value: "6" },
];

const clamp = (value: number, min: number, max: number): number => {
  if (Number.isNaN(value)) return min;
  return Math.min(max, Math.max(min, value));
};

const toPositiveInteger = (value: string | undefined): number | undefined => {
  if (!value) return undefined;

  const parsed = Number(value);
  if (!Number.isFinite(parsed) || parsed < 0) return undefined;

  return Math.floor(parsed);
};

const readStepOrValue = (
  token: string | undefined,
  fallback: number,
  min: number,
  max: number,
): number => {
  if (!token) return fallback;

  const stepMatch = token.match(/^\*\/(\d+)$/);
  if (stepMatch?.[1]) {
    return clamp(Number(stepMatch[1]), min, max);
  }

  const directValue = toPositiveInteger(token);
  if (directValue === undefined) return fallback;

  return clamp(directValue, min, max);
};

const readWeekdays = (token: string | undefined): string[] => {
  if (!token || token === "*") return ["1"];

  return token
    .split(",")
    .map((day) => day.trim())
    .filter((day) => /^\d$/.test(day))
    .map((day) => clamp(Number(day), 0, 6).toString());
};

export const deriveScheduleFields = (data: Record<string, unknown>): Record<string, unknown> => {
  const fallback = {
    secondsBetweenTriggers: 30,
    monthsBetweenTriggers: 1,
    hoursBetweenTriggers: 1,
    minutesBetweenTriggers: 5,
    triggerAtWeekdays: ["1"],
    triggerAtDayOfMonth: 1,
    triggerAtHour: "1",
    triggerAtMinute: 0,
  };

  const triggerInterval = String(data.triggerInterval || "days").toLowerCase();
  const cronExpression = String(data.cronExpression || "").trim();

  if (!cronExpression) return fallback;

  const allTokens = cronExpression.split(/\s+/).filter(Boolean);

  // Support both 5-field (minute-based, Hangfire-compatible) and legacy
  // 6-field (leading seconds) expressions; normalize to the 5 trailing fields.
  const isSixField = allTokens.length >= 6;
  const t = isSixField ? allTokens.slice(1) : allTokens;
  if (t.length < 5) return fallback;

  switch (triggerInterval) {
    case "seconds": {
      return {
        ...fallback,
        secondsBetweenTriggers: isSixField ? readStepOrValue(allTokens[0], 30, 1, 59) : 30,
      };
    }

    case "minutes": {
      return {
        ...fallback,
        minutesBetweenTriggers: readStepOrValue(t[0], 5, 1, 59),
      };
    }

    case "hours": {
      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(t[0], 0, 0, 59),
        hoursBetweenTriggers: readStepOrValue(t[1], 1, 1, 23),
      };
    }

    case "days": {
      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(t[0], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(t[1], 1, 0, 23)),
      };
    }

    case "weeks": {
      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(t[0], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(t[1], 1, 0, 23)),
        triggerAtWeekdays: readWeekdays(t[4]),
      };
    }

    case "months": {
      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(t[0], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(t[1], 1, 0, 23)),
        triggerAtDayOfMonth: readStepOrValue(t[2], 1, 1, 31),
        monthsBetweenTriggers: t[3] === "*" ? 1 : readStepOrValue(t[3], 1, 1, 12),
      };
    }

    default:
      return fallback;
  }
};

const generateCronExpression = (data: Record<string, unknown>): string => {
  const triggerInterval = String(data.triggerInterval || "days").toLowerCase();
  if (triggerInterval === "custom") {
    return String(data.cronExpression || "");
  }

  // Hangfire recurring jobs only support 5-field (minute-based) cron
  // expressions, so no seconds field is emitted.
  switch (triggerInterval) {
    case "seconds": {
      // 5-field cron cannot express sub-minute intervals; the smallest
      // supported interval is every minute.
      return "* * * * *";
    }

    case "minutes": {
      const minutes = clamp(Number(data.minutesBetweenTriggers ?? 5), 1, 59);
      return `*/${minutes} * * * *`;
    }

    case "hours": {
      const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
      const hours = clamp(Number(data.hoursBetweenTriggers ?? 1), 1, 23);
      // `*/n` only spaces evenly when n divides 24; otherwise it still fires
      // at clock hours divisible by n (cron stepping) — acceptable here since
      // there is no runtime recurrence filtering.
      return `${minute} */${hours} * * *`;
    }

    case "days": {
      const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
      const hour = clamp(Number(data.triggerAtHour ?? 0), 0, 23);
      return `${minute} ${hour} * * *`;
    }

    case "weeks": {
      const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
      const hour = clamp(Number(data.triggerAtHour ?? 0), 0, 23);
      const weekdays = Array.isArray(data.triggerAtWeekdays)
        ? (data.triggerAtWeekdays as unknown[])
            .map((d) => clamp(Number(d), 0, 6))
            .filter((d) => Number.isFinite(d))
            .sort((a, b) => a - b)
        : [1];
      const daysOfWeek = weekdays.length === 0 ? "*" : weekdays.join(",");
      return `${minute} ${hour} * * ${daysOfWeek}`;
    }

    case "months": {
      const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
      const hour = clamp(Number(data.triggerAtHour ?? 0), 0, 23);
      // Cap at 28 so the day exists in every month (day 30 would skip February).
      const dayOfMonth = clamp(Number(data.triggerAtDayOfMonth ?? 1), 1, 28);
      const months = clamp(Number(data.monthsBetweenTriggers ?? 1), 1, 12);
      // `*/n` only spaces evenly when n divides 12; prefer restricting n to
      // divisors of 12 (1, 2, 3, 4, 6, 12) — non-divisors still emit `*/n`
      // with cron-stepping semantics.
      return 12 % months === 0
        ? `${minute} ${hour} ${dayOfMonth} */${months} *`
        : `${minute} ${hour} ${dayOfMonth} * *`;
    }

    default: {
      const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
      const hour = clamp(Number(data.triggerAtHour ?? 0), 0, 23);
      return `${minute} ${hour} * * *`;
    }
  }
};

// Sub-fields are transient (only `cronExpression` is persisted), so sibling
// values are missing from `data` on every change. Rebuild them from the
// stored cron before regenerating so one edit does not reset the others.
const regenerateCronExpression = (data: Record<string, unknown>): string =>
  generateCronExpression({ ...deriveScheduleFields(data), ...data });

export const NodeSchemaTriggerScheduleV1: NodeSchemaDefinition = {
  guide: NodeGuideTriggerScheduleV1,
  schema: {
    type: "schedule",
    category: "trigger",
    version: "v1",
    parameters: [
      {
        id: "triggerInterval",
        type: "select",
        label: "Trigger Interval",
        info: "Select how often the scheduler should run.",
        key: "triggerInterval",
        required: true,
        options: TRIGGER_INTERVAL_OPTIONS,
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
      },
      {
        id: "minutesBetweenTriggers",
        type: "number",
        label: "Minutes Between Triggers",
        info: "Must be in range 1-59",
        key: "minutesBetweenTriggers",
        min: 1,
        max: 59,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "minutes",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).minutesBetweenTriggers,
      },
      {
        id: "hoursBetweenTriggers",
        type: "number",
        label: "Hours Between Triggers",
        info: "Must be in range 1-23. Note: cron stepping fires at clock hours divisible by the interval when it does not divide 24 evenly.",
        key: "hoursBetweenTriggers",
        min: 1,
        max: 23,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "hours",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).hoursBetweenTriggers,
      },
      {
        id: "monthsBetweenTriggers",
        type: "number",
        label: "Months Between Triggers",
        info: "Prefer divisors of 12 (1, 2, 3, 4, 6, 12) — other values fire every month.",
        key: "monthsBetweenTriggers",
        min: 1,
        max: 12,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "months",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).monthsBetweenTriggers,
      },
      {
        id: "cronExpression",
        type: "text",
        label: "Cron Expression",
        info: "5-field cron expression (minute-based), e.g. */10 * * * *",
        key: "cronExpression",
        required: true,
        dependsOn: {
          key: "triggerInterval",
          value: "custom",
          operator: "equals",
        },
        disabled: (data: Record<string, unknown>) => data.triggerInterval !== "custom",
      },
      {
        id: "triggerAtDayOfMonth",
        type: "number",
        label: "Trigger at Day of Month",
        info: "Day of the month (1-28 so it exists in every month).",
        key: "triggerAtDayOfMonth",
        min: 1,
        max: 28,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "months",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtDayOfMonth,
      },
      {
        id: "triggerAtWeekdays",
        type: "multiselect",
        label: "Trigger at Weekdays",
        info: "Days of the week to trigger on.",
        key: "triggerAtWeekdays",
        required: true,
        options: WEEKDAY_OPTIONS,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "weeks",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtWeekdays,
      },
      {
        id: "triggerAtHour",
        type: "select",
        label: "Trigger at Hour",
        info: "Hour of the day.",
        key: "triggerAtHour",
        required: true,
        options: HOUR_OPTIONS,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: ["days", "weeks", "months"],
          operator: "in",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) => deriveScheduleFields(data).triggerAtHour,
      },
      {
        id: "triggerAtMinute",
        type: "number",
        label: "Trigger at Minute",
        info: "Must be in range 0-59",
        key: "triggerAtMinute",
        min: 0,
        max: 59,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: ["hours", "days", "weeks", "months"],
          operator: "in",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: regenerateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) => deriveScheduleFields(data).triggerAtMinute,
      },
      {
        id: "output",
        type: "display",
        label: "Cron Preview",
        key: "output",
        displayValue: (data) => `${String(data.cronExpression || "")}`,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      triggerInterval: "days",
      cronExpression: "",
    },
    settings: {},
  },
  transform: (node) => {
    const normalizedTriggerInterval = String(
      node.parameters?.triggerInterval || "days",
    ).toLowerCase();
    const storedCronExpression = String(node.parameters?.cronExpression || "").trim();
    const normalizedCronExpression =
      normalizedTriggerInterval === "custom"
        ? storedCronExpression
        : storedCronExpression || generateCronExpression(node.parameters || {});

    return {
      ...node,
      parameters: {
        triggerInterval: normalizedTriggerInterval,
        cronExpression: normalizedCronExpression,
      },
    };
  },
};
