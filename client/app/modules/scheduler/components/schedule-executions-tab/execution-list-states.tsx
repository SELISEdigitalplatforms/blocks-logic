import { ReactNode } from "react";
import { Link } from "react-router";
import { History, Search } from "lucide-react";
import { useScopedPath } from "@seliseblocks/genesis-os";
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
    return `Next run ${formatInZone(schedule.nextRunAtUtc, tz, "datetime")} (${tz})`;
  }
  if (!schedule.isActive) return "This schedule is inactive. Activate it to start running.";
  if ("nextRunAtUtc" in schedule) return "No upcoming run in the current date window.";
  return "Runs will appear here after the schedule fires.";
}

export const NoExecutionsYet = ({ schedule, tz }: Readonly<{ schedule: ISchedule; tz: string }>) => (
  <StateLayout icon={<History className="h-7 w-7" />} title="No executions yet">
    <Message>{emptyStateMessage(schedule, tz)}</Message>
  </StateLayout>
);

export const NoMatchingExecutions = () => (
  <StateLayout icon={<Search className="h-7 w-7" />} title="No executions match this filter." />
);

export const ExecutionListError = ({ onRetry }: Readonly<{ onRetry: () => void }>) => (
  <StateLayout icon={<History className="h-7 w-7" />} title="Couldn't load executions">
    <Button type="button" variant="outline" size="sm" className="mt-6" onClick={onRetry}>
      Retry
    </Button>
  </StateLayout>
);

export const ScheduleGone = () => {
  const scoped = useScopedPath();
  return (
    <StateLayout icon={<History className="h-7 w-7" />} title="This schedule no longer exists.">
      <Button asChild variant="outline" size="sm" className="mt-6">
        <Link to={scoped("schedule")}>Back to schedules</Link>
      </Button>
    </StateLayout>
  );
};
