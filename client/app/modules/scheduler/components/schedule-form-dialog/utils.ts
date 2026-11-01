import { z } from "zod";
import { format } from "date-fns";
import { isParseableUrl, parseDateString } from "@/lib/utils";
import { formatInZone, UTC, zonedDateToUtcIso } from "../../utils/timezone";
import { validateCronExpression } from "./cron-validation";

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
    name: z.string().trim().min(1, "Name is required").max(100, "Name cannot exceed 100 characters"),
    description: z.string().max(500, "Description cannot exceed 500 characters"),
    webhook: z.object({

      url: z.string().min(1, "Webhook URL is required").refine(isParseableUrl, "Enter a valid URL"),
      method: z.string().default("POST"),
      headers: webhookHeadersSchema,
      signingSecret: z.string(),
    }),
    cronExpression: z
      .string()
      .min(1, "Cron expression is required")
      .superRefine((value, ctx) => {
        if (!value) return;
        const error = validateCronExpression(value);
        if (error) ctx.addIssue({ code: z.ZodIssueCode.custom, message: error });
      }),
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
    timeZone: z.string().min(1, "Timezone is required"),
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
  timeZone: UTC,
  isActive: true,
};

export const toIsoOrNull = (value?: string) =>
  value ? new Date(value).toISOString() : null;

/** The stored id; UTC is stored as null so existing and new UTC schedules look the same. */
export const toPayloadTimeZone = (timeZone: string): string | null => (timeZone === UTC ? null : timeZone);

/**
 * A picked `yyyy-MM-dd` as the instant the API stores. UTC keeps today's exact `toIsoOrNull` strings;
 * another zone sends local 00:00 of that day in the zone.
 */
export const toPayloadDate = (value: string | undefined, timeZone: string): string | null => {
  if (timeZone === UTC) return toIsoOrNull(value);
  return value ? zonedDateToUtcIso(value, timeZone) : null;
};

export const TIME_ZONE_NOT_RECOGNISED = "Time zone is not recognised";

/** The server's time zone refusal, to show under the Timezone field; null for any other error. */
export const timeZoneErrorFrom = (errors?: Record<string, string> | null): string | null =>
  errors?.validation_failed === TIME_ZONE_NOT_RECOGNISED ? TIME_ZONE_NOT_RECOGNISED : null;

/** A stored instant as the form's `yyyy-MM-dd`, read in the schedule's zone (UTC unchanged). */
export const toFormDate = (iso: string | null | undefined, timeZone: string): string => {
  if (!iso) return "";
  if (timeZone === UTC) return format(parseDateString(iso), "yyyy-MM-dd");
  return formatInZone(iso, timeZone, "yyyy-MM-dd");
};

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
