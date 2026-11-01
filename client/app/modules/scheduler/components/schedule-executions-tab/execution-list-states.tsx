import { ReactNode } from "react";
import { Link } from "react-router";
import { History, Search } from "lucide-react";
import { useScopedPath, useT, tStatic } from "@seliseblocks/genesis-os";
import { Button } from "@/components/ui-kits/button/button";
import { ISchedule } from "../../types/schedule.service.type";
import { formatInZone } from "../../utils/timezone";

interface StateLayoutProps {
  icon: ReactNode;
  title: string;
  children?: ReactNode;
}

const StateLayout = ({ icon, title, children }: Readonly<StateLayoutProps>) => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">{icon}</div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">{title}</h4>
    {children}
  </div>
);

const Message = ({ children }: Readonly<{ children: ReactNode }>) => (
  <p className="mt-2 max-w-md text-sm text-muted-foreground">{children}</p>
);

/** The "no executions yet" text; `nextRunAtUtc` only exists once SPEC6 ships, so its absence is a case. */
export function emptyStateMessage(schedule: ISchedule, tz: string): string {
  if (schedule.nextRunAtUtc) {
    return tStatic("logic.scheduler", "EXECUTION_LIST_STATES.NEXT_RUN", { value: formatInZone(schedule.nextRunAtUtc, tz, "datetime"), tz });
  }
  if (!schedule.isActive) return tStatic("logic.scheduler", "EXECUTION_LIST_STATES.THIS_SCHEDULE_IS_INACTIVE");
  if ("nextRunAtUtc" in schedule) return tStatic("logic.scheduler", "EXECUTION_LIST_STATES.NO_UPCOMING_RUN_IN");
  return tStatic("logic.scheduler", "EXECUTION_LIST_STATES.RUNS_WILL_APPEAR_HERE");
}

export const NoExecutionsYet = ({ schedule, tz }: Readonly<{ schedule: ISchedule; tz: string }>) => {
  const { t } = useT("logic.scheduler");
  return (
    <StateLayout icon={<History className="h-7 w-7" />} title={t("EXECUTION_LIST_STATES.NO_EXECUTIONS_YET")}>
      <Message>{emptyStateMessage(schedule, tz)}</Message>
    </StateLayout>
  );
};

export const NoMatchingExecutions = () => {
  const { t } = useT("logic.scheduler");
  return (
    <StateLayout icon={<Search className="h-7 w-7" />} title={t("EXECUTION_LIST_STATES.NO_EXECUTIONS_MATCH_THIS")} />
  );
};

export const ExecutionListError = ({ onRetry }: Readonly<{ onRetry: () => void }>) => {
  const { t } = useT("logic.scheduler");
  return (
    <StateLayout icon={<History className="h-7 w-7" />} title={t("EXECUTION_LIST_STATES.COULDN_T_LOAD_EXECUTIONS")}>
      <Button type="button" variant="outline" size="sm" className="mt-6" onClick={onRetry}>
        {t("common:RETRY")}
      </Button>
    </StateLayout>
  );
};

export const ScheduleGone = () => {
  const { t } = useT("logic.scheduler");
  const scoped = useScopedPath();
  return (
    <StateLayout icon={<History className="h-7 w-7" />} title={t("EXECUTION_LIST_STATES.THIS_SCHEDULE_NO_LONGER")}>
      <Button asChild variant="outline" size="sm" className="mt-6">
        <Link to={scoped("schedule")}>{t("EXECUTION_LIST_STATES.BACK_TO_SCHEDULES")}</Link>
      </Button>
    </StateLayout>
  );
};
