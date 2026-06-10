import { useProjectStore } from "@seliseblocks/blocks-kit";
import { NodeSchemaDefinition } from "./node-schema.type";

const TRIGGER_INTERVAL_OPTIONS = [
  { label: "Seconds", value: "seconds" },
  { label: "Minutes", value: "minutes" },
  { label: "Hours", value: "hours" },
  { label: "Days", value: "days" },
  { label: "Weeks", value: "weeks" },
  { label: "Months", value: "months" },
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

const deriveScheduleFields = (
  data: Record<string, unknown>,
): Record<string, unknown> => {
  const fallback = {
    secondsBetweenTriggers: 30,
    daysBetweenTriggers: 1,
    weeksBetweenTriggers: 1,
    monthsBetweenTriggers: 1,
    hoursBetweenTriggers: 1,
    minutesBetweenTriggers: 5,
    triggerAtWeek: "1",
    triggerAtDayOfMonth: 1,
    triggerAtHour: "1",
    triggerAtMinute: 0,
  };

  const triggerInterval = String(data.triggerInterval || "days").toLowerCase();
  const cronExpression = String(data.cronExpression || "").trim();

  if (!cronExpression) return fallback;

  const [cronPart, commentPart = ""] = cronExpression.split("#");
  const cronTokens = cronPart.trim().split(/\s+/).filter(Boolean);

  switch (triggerInterval) {
    case "seconds": {
      if (cronTokens.length < 6) return fallback;

      return {
        ...fallback,
        secondsBetweenTriggers: readStepOrValue(cronTokens[0], 30, 1, 59),
      };
    }

    case "minutes": {
      if (cronTokens.length < 6) return fallback;

      return {
        ...fallback,
        minutesBetweenTriggers: readStepOrValue(cronTokens[1], 5, 1, 59),
      };
    }

    case "hours": {
      if (cronTokens.length < 6) return fallback;

      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(cronTokens[1], 0, 0, 59),
        hoursBetweenTriggers: readStepOrValue(cronTokens[2], 1, 1, 23),
      };
    }

    case "days": {
      if (cronTokens.length < 6) return fallback;

      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(cronTokens[1], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(cronTokens[2], 1, 0, 23)),
        daysBetweenTriggers: readStepOrValue(cronTokens[3], 1, 1, 31),
      };
    }

    case "weeks": {
      if (cronTokens.length < 6) return fallback;

      const weeksMatch = commentPart.match(/every\s+(\d+)\s+weeks/i);

      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(cronTokens[1], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(cronTokens[2], 1, 0, 23)),
        triggerAtWeek: String(readStepOrValue(cronTokens[5], 1, 0, 6)),
        weeksBetweenTriggers: weeksMatch?.[1]
          ? Math.max(1, Number(weeksMatch[1]))
          : fallback.weeksBetweenTriggers,
      };
    }

    case "months": {
      if (cronTokens.length < 6) return fallback;

      return {
        ...fallback,
        triggerAtMinute: readStepOrValue(cronTokens[1], 0, 0, 59),
        triggerAtHour: String(readStepOrValue(cronTokens[2], 1, 0, 23)),
        triggerAtDayOfMonth: readStepOrValue(cronTokens[3], 1, 1, 31),
        monthsBetweenTriggers:
          cronTokens[4] === "*" ? 1 : readStepOrValue(cronTokens[4], 1, 1, 12),
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

  const minute = clamp(Number(data.triggerAtMinute ?? 0), 0, 59);
  const hour = clamp(Number(data.triggerAtHour ?? 0), 0, 23);
  const weekday = clamp(Number(data.triggerAtWeek ?? 1), 0, 6);
  const dayOfMonth = clamp(Number(data.triggerAtDayOfMonth ?? 1), 1, 31);
  const seconds = clamp(Number(data.secondsBetweenTriggers ?? 30), 1, 59);
  const minutes = clamp(Number(data.minutesBetweenTriggers ?? 5), 1, 59);
  const hours = clamp(Number(data.hoursBetweenTriggers ?? 1), 1, 23);
  const days = clamp(Number(data.daysBetweenTriggers ?? 1), 1, 31);
  const months = clamp(Number(data.monthsBetweenTriggers ?? 1), 1, 12);
  const weeksStep = Math.max(1, Number(data.weeksBetweenTriggers ?? 1));

  switch (triggerInterval) {
    case "seconds":
      return `*/${seconds} * * * * *`;
    case "minutes":
      return `0 */${minutes} * * * *`;

    case "hours":
      return `0 ${minute} */${hours} * * *`;

    case "days":
      return `0 ${minute} ${hour} */${days} * *`;

    case "weeks":
      return weeksStep === 1
        ? `0 ${minute} ${hour} * * ${weekday}`
        : `0 ${minute} ${hour} * * ${weekday} # every ${weeksStep} weeks`;

    case "months":
      return months === 1
        ? `0 ${minute} ${hour} ${dayOfMonth} * *`
        : `0 ${minute} ${hour} ${dayOfMonth} */${months} *`;

    default:
      return `0 ${minute} ${hour} */${days} * *`;
  }
};

export const NodeSchemaTriggerBlockscheduleV1: NodeSchemaDefinition = {
  schema: {
    type: "blockschedule",
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
          cronExpression: generateCronExpression(data),
        }),
      },
      {
        id: "secondsBetweenTriggers",
        type: "number",
        label: "Seconds Between Triggers",
        info: "Must be in range 1-59",
        key: "secondsBetweenTriggers",
        min: 1,
        max: 59,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "seconds",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).secondsBetweenTriggers,
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
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).minutesBetweenTriggers,
      },
      {
        id: "hoursBetweenTriggers",
        type: "number",
        label: "Hours Between Triggers",
        info: "Must be in range 1-23",
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
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).hoursBetweenTriggers,
      },
      {
        id: "daysBetweenTriggers",
        type: "number",
        label: "Days Between Triggers",
        info: "Must be in range 1-31",
        key: "daysBetweenTriggers",
        min: 1,
        max: 31,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "days",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).daysBetweenTriggers,
      },
      {
        id: "weeksBetweenTriggers",
        type: "number",
        label: "Weeks Between Triggers",
        info: "Would run every week unless specified otherwise",
        key: "weeksBetweenTriggers",
        min: 1,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "weeks",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).weeksBetweenTriggers,
      },
      {
        id: "monthsBetweenTriggers",
        type: "number",
        label: "Months Between Triggers",
        info: "Would run every month unless specified otherwise",
        key: "monthsBetweenTriggers",
        min: 1,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "months",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).monthsBetweenTriggers,
      },
      {
        id: "cronExpression",
        type: "text",
        label: "Cron Expression",
        info: "Generated from trigger interval and timing fields.",
        key: "cronExpression",
        required: true,
        dependsOn: {
          key: "triggerInterval",
          value: "custom",
          operator: "equals",
        },
        disabled: (data: Record<string, unknown>) =>
          data.triggerInterval !== "custom",
      },
      {
        id: "triggerAtDayOfMonth",
        type: "number",
        label: "Trigger at Day of Month",
        info: "Day of the month.",
        key: "triggerAtDayOfMonth",
        min: 1,
        max: 31,
        required: true,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "months",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtDayOfMonth,
      },
      {
        id: "triggerAtWeek",
        type: "select",
        label: "Trigger at Weekday",
        info: "Day of the week.",
        key: "triggerAtWeek",
        required: true,
        options: WEEKDAY_OPTIONS,
        transient: true,
        dependsOn: {
          key: "triggerInterval",
          value: "weeks",
          operator: "equals",
        },
        onChange: (_value: unknown, data: Record<string, unknown>) => ({
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtWeek,
      },
      {
        id: "triggerAtHour",
        type: "select",
        label: "Trigger at Hour",
        info: "Hour of day in 12-hour format.",
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
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtHour,
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
          cronExpression: generateCronExpression(data),
        }),
        defaultValue: (data: Record<string, unknown>) =>
          deriveScheduleFields(data).triggerAtMinute,
      },
      {
        id: "startDate",
        type: "text",
        label: "Start Date",
        info: "ISO date-time, e.g. 2026-04-29T21:28:29.215Z",
        key: "startDate",
        required: true,
      },
      {
        id: "endDate",
        type: "text",
        label: "End Date",
        info: "ISO date-time, e.g. 2026-04-29T21:28:29.215Z",
        key: "endDate",
        required: true,
      },
      {
        id: "payload",
        type: "textarea",
        label: "Payload",
        info: "Payload body passed to scheduler.",
        key: "payload",
        required: true,
        placeholder: "{}",
      },
      {
        id: "output",
        type: "display",
        label: "Payload Preview",
        key: "output",
        displayValue: (data) => `${String(data.cronExpression || "")}`,
      },
    ],
    settings: [],
  },
  defaults: {
    parameters: {
      payload: "",
      triggerInterval: "days",
      cronExpression: "0 0 1 */1 * *",
      startDate: "",
      endDate: "",
      projectKey: "",
    },
    settings: {},
  },
  transform: (node) => {
    const selectedProject = useProjectStore.getState().selectedProject;
    const normalizedTriggerInterval = String(
      node.parameters?.triggerInterval || "days",
    ).toLowerCase();
    const storedCronExpression = String(
      node.parameters?.cronExpression || "",
    ).trim();
    const normalizedCronExpression =
      normalizedTriggerInterval === "custom"
        ? storedCronExpression
        : storedCronExpression || generateCronExpression(node.parameters || {});

    return {
      ...node,
      parameters: {
        payload: node.parameters?.payload ?? "",
        triggerInterval: normalizedTriggerInterval,
        cronExpression: normalizedCronExpression,
        startDate: node.parameters?.startDate ?? "",
        endDate: node.parameters?.endDate ?? "",
        scheduleEventOperation: Number(
          node.parameters?.scheduleEventOperation ?? 0,
        ),
        projectKey: selectedProject?.tenantId ?? "",
      },
    };
  },
};
