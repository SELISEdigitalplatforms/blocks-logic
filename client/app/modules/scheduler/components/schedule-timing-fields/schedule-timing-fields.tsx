import { formatDate, parseDateString } from "@/lib/utils";
import { ISchedule } from "../../types/schedule.service.type";
import { localDate } from "../../utils/execution-date-range";
import { offsetLabel, UTC } from "../../utils/timezone";
import { toFormDate } from "../schedule-form-dialog/utils";
import { ZonedTime } from "../schedule-executions-tab/zoned-time";
import { useT } from "@seliseblocks/genesis-os";

/** UTC dates show exactly as before; another zone shows the calendar day in that zone, same format. */
export const scheduleDate = (iso: string, timeZone: string) =>
  timeZone === UTC ? formatDate(parseDateString(iso)) : formatDate(localDate(toFormDate(iso, timeZone)));

const Field = ({ label, children }: Readonly<{ label: string; children: React.ReactNode }>) => (
  <div>
    <span className="text-xs font-medium text-muted-foreground">{label}</span>
    <p className="mt-0.5 text-sm font-medium">{children}</p>
  </div>
);

/** Start, End, Time zone and Next run of the details page's "Timing & Schedule" card, in the schedule's zone. */
export const ScheduleTimingFields = ({ schedule }: Readonly<{ schedule: ISchedule }>) => {
  const { t } = useT("logic.scheduler");
  const timeZone = schedule.timeZone ?? UTC;

  return (
    <div className="grid grid-cols-2 gap-4 border-t pt-3">
      <Field label={t("SCHEDULE_TIMING_FIELDS.START_DATE")}>
        {schedule.startDate ? scheduleDate(schedule.startDate, timeZone) : t("SCHEDULE_TIMING_FIELDS.IMMEDIATE_NO_START_DATE")}
      </Field>
      <Field label={t("SCHEDULE_TIMING_FIELDS.END_DATE")}>
        {schedule.endDate ? scheduleDate(schedule.endDate, timeZone) : t("SCHEDULE_TIMING_FIELDS.INDEFINITE_NO_END_DATE")}
      </Field>
      <Field label={t("SCHEDULE_TIMING_FIELDS.TIME_ZONE")}>
        {timeZone} ({offsetLabel(timeZone)})
      </Field>
      <Field label={t("SCHEDULE_TIMING_FIELDS.NEXT_RUN")}>
        <ZonedTime iso={schedule.nextRunAtUtc} tz={timeZone} />
      </Field>
    </div>
  );
};
