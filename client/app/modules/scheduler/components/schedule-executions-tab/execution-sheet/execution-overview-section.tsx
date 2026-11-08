import { ReactNode } from "react";
import { IScheduleExecution } from "../../../types/schedule.service.type";
import { OutcomeBadge } from "../outcome-badge";
import { formatDuration } from "../status-label";
import { StatusChip } from "../status-chip";
import { ZonedTime } from "../zoned-time";
import { ExecutionSection } from "./execution-section";
import { useT } from "@seliseblocks/genesis-os";

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
  const { t } = useT("logic.scheduler");
  if (!stored) return <span className="text-muted-foreground">—</span>;
  const renamed = !!current && current !== stored;
  return (
    <span className="break-words">
      {stored}
      {renamed && <span className="ml-1 text-muted-foreground">{t("EXECUTION_OVERVIEW_SECTION.NOW", { current })}</span>}
    </span>
  );
};

export const ExecutionOverviewSection = ({
  execution,
  statusLabel,
  tz,
  currentName,
}: Readonly<ExecutionOverviewSectionProps>) => {
  const { t } = useT("logic.scheduler");
  return (
    <ExecutionSection title={t("EXECUTION_OVERVIEW_SECTION.OVERVIEW")}>
      <dl className="grid grid-cols-[140px_1fr] items-center gap-x-4 gap-y-2 text-sm">
        <Row label={t("EXECUTION_OVERVIEW_SECTION.OUTCOME")}>
          <OutcomeBadge outcome={execution.outcome} />
        </Row>
        <Row label={t("common:STATUS")}>
          <StatusChip label={statusLabel} />
        </Row>
        <Row label={t("EXECUTION_OVERVIEW_SECTION.FIRED_AT")}>
          <ZonedTime iso={execution.firedUtc} tz={tz} />
        </Row>
        <Row label={t("EXECUTION_OVERVIEW_SECTION.FINISHED_AT")}>
          <ZonedTime iso={execution.finishedUtc} tz={tz} />
        </Row>
        <Row label={t("EXECUTION_OVERVIEW_SECTION.DURATION")}>{formatDuration(execution.durationMs)}</Row>
        <Row label={t("EXECUTION_OVERVIEW_SECTION.SCHEDULE_NAME")}>
          <ScheduleName stored={execution.scheduleName} current={currentName} />
        </Row>
      </dl>
    </ExecutionSection>
  );
};
