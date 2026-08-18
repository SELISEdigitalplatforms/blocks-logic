import { z } from "zod";

const CRON_FIELD_PATTERN = "(?:\\*|\\d+)(?:-\\d+)?(?:\\/\\d+)?";
const CRON_EXPRESSION_REGEX = new RegExp(
  `^${CRON_FIELD_PATTERN}(?:,${CRON_FIELD_PATTERN})*(?:\\s+${CRON_FIELD_PATTERN}(?:,${CRON_FIELD_PATTERN})*){4}$`,
);

export const WEBHOOK_METHODS = ["POST", "GET", "PUT", "PATCH", "DELETE"] as const;

export const CRON_PRESETS = [
  { label: "Every minute", value: "* * * * *" },
  { label: "Hourly", value: "0 * * * *" },
  { label: "Daily 9 AM", value: "0 9 * * *" },
  { label: "Weekdays 9 AM", value: "0 9 * * MON-FRI" },
  { label: "Weekly Mon", value: "0 9 * * MON" },
  { label: "Monthly 1st", value: "0 9 1 * *" },
] as const;

const webhookHeadersSchema = z.array(
  z.object({
    key: z.string(),
    value: z.string(),
  }),
);

export const scheduleFormSchema = z
  .object({
    name: z.string().min(1, "Name is required"),
    description: z.string(),
    webhook: z.object({
      url: z.string().min(1, "Webhook URL is required").url("Enter a valid URL"),
      method: z.string().default("POST"),
      headers: webhookHeadersSchema,
      signingSecret: z.string(),
    }),
    cronExpression: z
      .string()
      .min(1, "Cron expression is required")
      .regex(CRON_EXPRESSION_REGEX, "Enter a valid 5-field cron expression"),
    payload: z.string().refine(
      (value) => {
        if (!value.trim()) return true;
        try {
          JSON.parse(value);
          return true;
        } catch {
          return false;
        }
      },
      { message: "Payload must be valid JSON" },
    ),
    startDate: z.string().optional(),
    endDate: z.string().optional(),
    isActive: z.boolean(),
  })
  .superRefine((values, ctx) => {
    if (
      values.startDate &&
      values.endDate &&
      new Date(values.endDate).getTime() < new Date(values.startDate).getTime()
    ) {
      ctx.addIssue({
        code: z.ZodIssueCode.custom,
        message: "End date must be after start date",
        path: ["endDate"],
      });
    }

    values.webhook.headers.forEach((header, index) => {
      if (header.key.trim() && !header.value.trim()) {
        ctx.addIssue({
          code: z.ZodIssueCode.custom,
          message: "Header value is required when a key is set",
          path: ["webhook", "headers", index, "value"],
        });
      }
    });
  });

export type ScheduleFormValues = z.infer<typeof scheduleFormSchema>;

export const scheduleFormDefaultValues: ScheduleFormValues = {
  name: "",
  description: "",
  webhook: {
    url: "",
    method: "POST",
    headers: [],
    signingSecret: "",
  },
  cronExpression: "",
  payload: "",
  startDate: "",
  endDate: "",
  isActive: true,
};

export const toIsoOrNull = (value?: string) =>
  value ? new Date(value).toISOString() : null;

export const webhookHeadersToArray = (
  headers?: Record<string, string> | null,
): { key: string; value: string }[] =>
  Object.entries(headers ?? {}).map(([key, value]) => ({ key, value }));

export const webhookHeadersToRecord = (
  headers: { key: string; value: string }[],
): Record<string, string> | null => {
  const record: Record<string, string> = {};
  headers.forEach((header) => {
    if (header.key.trim()) record[header.key.trim()] = header.value;
  });
  return Object.keys(record).length > 0 ? record : null;
};
