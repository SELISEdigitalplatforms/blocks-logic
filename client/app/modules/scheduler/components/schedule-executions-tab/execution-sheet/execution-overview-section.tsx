import { ReactNode } from "react";
import { IScheduleExecution } from "../../../types/schedule.service.type";
import { OutcomeBadge } from "../outcome-badge";
import { formatDuration } from "../status-label";
import { StatusChip } from "../status-chip";
import { ZonedTime } from "../zoned-time";
import { ExecutionSection } from "./execution-section";

interface ExecutionOverviewSectionProps {
  execution: IScheduleExecution;
  statusLabel: string;
  tz: string;
  /** The schedule's current name, to flag a rename since this run. */
  currentName?: string | null;
}

const Row = ({ label, children }: Readonly<{ label: string; children: ReactNode }>) => (
  <>
    <dt className="text-muted-foreground">{label}</dt>
    <dd className="min-w-0">{children}</dd>
  </>
);

const ScheduleName = ({ stored, current }: Readonly<{ stored?: string | null; current?: string | null }>) => {
  if (!stored) return <span className="text-muted-foreground">—</span>;
  const renamed = !!current && current !== stored;
  return (
    <span className="break-words">
      {stored}
      {renamed && <span className="ml-1 text-muted-foreground">(now: {current})</span>}
    </span>
  );
};

export const ExecutionOverviewSection = ({
  execution,
  statusLabel,
  tz,
  currentName,
}: Readonly<ExecutionOverviewSectionProps>) => (
  <ExecutionSection title="Overview">
    <dl className="grid grid-cols-[140px_1fr] items-center gap-x-4 gap-y-2 text-sm">
      <Row label="Outcome">
        <OutcomeBadge outcome={execution.outcome} />
      </Row>
      <Row label="Status">
        <StatusChip label={statusLabel} />
      </Row>
      <Row label="Fired at">
        <ZonedTime iso={execution.firedUtc} tz={tz} />
      </Row>
      <Row label="Finished at">
        <ZonedTime iso={execution.finishedUtc} tz={tz} />
      </Row>
      <Row label="Duration">{formatDuration(execution.durationMs)}</Row>
      <Row label="Schedule name">
        <ScheduleName stored={execution.scheduleName} current={currentName} />
      </Row>
    </dl>
  </ExecutionSection>
);
