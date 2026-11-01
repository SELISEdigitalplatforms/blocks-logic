import { ScheduleSnapshot } from "../parse-snapshot";
import { formatInZone } from "../../../utils/timezone";
import { ExecutionSection } from "./execution-section";
import { useT } from "@seliseblocks/genesis-os";

const windowBound = (iso: string | null, tz: string, fallback: string) =>
  iso ? formatInZone(iso, tz, "datetime") : fallback;

export const ExecutionScheduleSection = ({ snapshot }: Readonly<{ snapshot: ScheduleSnapshot }>) => {
  const { t } = useT("logic.scheduler");
  const tz = snapshot.timeZone || "UTC";

  return (
    <ExecutionSection title={t("EXECUTION_SCHEDULE_SECTION.SCHEDULE_AT_FIRE_TIME")}>
      <dl className="grid grid-cols-[140px_1fr] items-center gap-x-4 gap-y-2 text-sm">
        <dt className="text-muted-foreground">{t("EXECUTION_SCHEDULE_SECTION.CRON")}</dt>
        <dd>
          {snapshot.cronExpression ? (
            <code className="rounded bg-muted px-2.5 py-1 font-mono text-sm font-semibold text-foreground">
              {snapshot.cronExpression}
            </code>
          ) : (
            "—"
          )}
        </dd>
        <dt className="text-muted-foreground">{t("EXECUTION_SCHEDULE_SECTION.TIME_ZONE")}</dt>
        <dd>{tz}</dd>
        <dt className="text-muted-foreground">{t("EXECUTION_SCHEDULE_SECTION.ACTIVE_WINDOW")}</dt>
        <dd className="break-words">
          {windowBound(snapshot.startDate, tz, t("EXECUTION_SCHEDULE_SECTION.NO_START"))} → {windowBound(snapshot.endDate, tz, t("EXECUTION_SCHEDULE_SECTION.NO_END"))}
        </dd>
        {snapshot.description && (
          <>
            <dt className="text-muted-foreground">{t("common:DESCRIPTION")}</dt>
            <dd className="break-words">{snapshot.description}</dd>
          </>
        )}
      </dl>
    </ExecutionSection>
  );
};
