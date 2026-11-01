"use client";
import { useMemo } from "react";
import { useDebounce } from "@/hooks/use-debounce";
import { usePreviewScheduleRuns } from "../../hooks/use-schedule-api";
import { formatInZone } from "../../utils/timezone";
import { validateCronExpression } from "../schedule-form-dialog/cron-validation";
import { toPayloadDate, toPayloadTimeZone } from "../schedule-form-dialog/utils";

export const PREVIEW_DEBOUNCE_MS = 400;
const PREVIEW_COUNT = 3;

interface ScheduleNextRunsProps {
  cronExpression: string;
  timeZone: string;
  /** The form's `yyyy-MM-dd` values. */
  startDate?: string;
  endDate?: string;
}

const Note = ({ children }: Readonly<{ children: string }>) => (
  <p className="text-xs text-muted-foreground">{children}</p>
);

/** Up to three upcoming fires in the chosen zone; at most one request per 400 ms of idle input. */
export const ScheduleNextRuns = ({ cronExpression, timeZone, startDate, endDate }: Readonly<ScheduleNextRunsProps>) => {
  const input = useMemo(
    () => ({
      cronExpression,
      timeZone: toPayloadTimeZone(timeZone),
      startDate: toPayloadDate(startDate, timeZone),
      endDate: toPayloadDate(endDate, timeZone),
      count: PREVIEW_COUNT,
    }),
    [cronExpression, timeZone, startDate, endDate],
  );
  const debounced = useDebounce(input, PREVIEW_DEBOUNCE_MS);
  const cronIsValid = validateCronExpression(cronExpression) === null;
  const debouncedCronIsValid = validateCronExpression(debounced.cronExpression) === null;
  const { data, isLoading, isError } = usePreviewScheduleRuns(debounced, { enabled: debouncedCronIsValid });

  const body = () => {
    if (!cronIsValid) return <Note>Enter a valid cron to preview</Note>;
    if (isLoading || !debouncedCronIsValid) return <Note>Calculating…</Note>;
    if (isError || !data?.isSuccess || !data.data) return null;
    const occurrences = data.data.occurrencesUtc;
    if (occurrences.length === 0) return <Note>No upcoming run in this date window.</Note>;
    return (
      <ul className="space-y-0.5 font-mono text-xs">
        {occurrences.map((occurrence) => (
          <li key={occurrence}>{formatInZone(occurrence, timeZone, "datetime")}</li>
        ))}
      </ul>
    );
  };

  const content = body();
  if (!content) return null;

  return (
    <div className="space-y-1" aria-live="polite">
      <span className="text-xs font-medium text-muted-foreground">Next runs</span>
      {content}
    </div>
  );
};
