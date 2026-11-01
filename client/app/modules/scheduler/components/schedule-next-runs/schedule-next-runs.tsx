"use client";
import { useMemo } from "react";
import { useDebounce } from "@/hooks/use-debounce";
import { usePreviewScheduleRuns } from "../../hooks/use-schedule-api";
import { formatInZone } from "../../utils/timezone";
import { validateCronExpression } from "../schedule-form-dialog/cron-validation";
import { toPayloadDate, toPayloadTimeZone } from "../schedule-form-dialog/utils";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("logic.scheduler");
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
    if (!cronIsValid) return <Note>{t("SCHEDULE_NEXT_RUNS.ENTER_A_VALID_CRON")}</Note>;
    if (isLoading || !debouncedCronIsValid) return <Note>{t("SCHEDULE_NEXT_RUNS.CALCULATING")}</Note>;
    if (isError || !data?.isSuccess || !data.data) return null;
    const occurrences = data.data.occurrencesUtc;
    if (occurrences.length === 0) return <Note>{t("SCHEDULE_NEXT_RUNS.NO_UPCOMING_RUN_IN")}</Note>;
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
      <span className="text-xs font-medium text-muted-foreground">{t("SCHEDULE_NEXT_RUNS.NEXT_RUNS")}</span>
      {content}
    </div>
  );
};
