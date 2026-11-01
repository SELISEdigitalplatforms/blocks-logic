import { ScheduleSnapshot } from "../parse-snapshot";
import { formatInZone } from "../../../utils/timezone";
import { ExecutionSection } from "./execution-section";

const windowBound = (iso: string | null, tz: string, fallback: string) =>
  iso ? formatInZone(iso, tz, "datetime") : fallback;

export const ExecutionScheduleSection = ({ snapshot }: Readonly<{ snapshot: ScheduleSnapshot }>) => {
  const tz = snapshot.timeZone || "UTC";

  return (
    <ExecutionSection title="Schedule at fire time">
      <dl className="grid grid-cols-[140px_1fr] items-center gap-x-4 gap-y-2 text-sm">
        <dt className="text-muted-foreground">Cron</dt>
        <dd>
          {snapshot.cronExpression ? (
            <code className="rounded bg-muted px-2.5 py-1 font-mono text-sm font-semibold text-foreground">
              {snapshot.cronExpression}
            </code>
          ) : (
            "—"
          )}
        </dd>
        <dt className="text-muted-foreground">Time zone</dt>
        <dd>{tz}</dd>
        <dt className="text-muted-foreground">Active window</dt>
        <dd className="break-words">
          {windowBound(snapshot.startDate, tz, "No start")} → {windowBound(snapshot.endDate, tz, "No end")}
        </dd>
        {snapshot.description && (
          <>
            <dt className="text-muted-foreground">Description</dt>
            <dd className="break-words">{snapshot.description}</dd>
          </>
        )}
      </dl>
    </ExecutionSection>
  );
};
