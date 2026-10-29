import { formatDate, parseDateString } from "@/lib/utils";
import { ISchedule } from "../../types/schedule.service.type";
import { localDate } from "../../utils/execution-date-range";
import { UTC } from "../../utils/timezone";
import { toFormDate } from "../schedule-form-dialog/utils";
import { ZonedTime } from "../schedule-executions-tab/zoned-time";

/** UTC dates show exactly as before; another zone shows the calendar day in that zone, same format. */
export const scheduleDate = (iso: string, timeZone: string) =>
  timeZone === UTC ? formatDate(parseDateString(iso)) : formatDate(localDate(toFormDate(iso, timeZone)));

const Field = ({ label, children }: Readonly<{ label: string; children: React.ReactNode }>) => (
  <div>
    <span className="text-xs font-medium text-muted-foreground">{label}</span>
    <p className="mt-0.5 text-sm font-medium">{children}</p>
  </div>
);

/** Start, End and Next run of the details page's "Timing & Schedule" card, in the schedule's zone. */
export const ScheduleTimingFields = ({ schedule }: Readonly<{ schedule: ISchedule }>) => {
  const timeZone = schedule.timeZone ?? UTC;

  return (
    <div className="grid grid-cols-2 gap-4 border-t pt-3">
      <Field label="Start Date">
        {schedule.startDate ? scheduleDate(schedule.startDate, timeZone) : "Immediate (No start date)"}
      </Field>
      <Field label="End Date">
        {schedule.endDate ? scheduleDate(schedule.endDate, timeZone) : "Indefinite (No end date)"}
      </Field>
      <Field label="Next run">
        <ZonedTime iso={schedule.nextRunAtUtc} tz={timeZone} />
      </Field>
    </div>
  );
};
