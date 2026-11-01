import { SheetDescription, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { IScheduleExecution } from "../../../types/schedule.service.type";
import { formatInZone } from "../../../utils/timezone";
import { OutcomeBadge } from "../outcome-badge";
import { StatusChip } from "../status-chip";

interface ExecutionSheetHeaderProps {
  execution: IScheduleExecution | null;
  statusLabel: string;
  tz: string;
  /** With no execution: true shows a skeleton, false only the title. */
  loading?: boolean;
}

const HeaderPlaceholder = ({ loading }: Readonly<{ loading: boolean }>) => (
  <>
    <SheetDescription className="sr-only">{loading ? "Loading execution" : "Execution unavailable"}</SheetDescription>
    {loading && (
      <>
        <Skeleton className="h-4 w-48" />
        <Skeleton className="h-6 w-40" />
      </>
    )}
  </>
);

export const ExecutionSheetHeader = ({
  execution,
  statusLabel,
  tz,
  loading = false,
}: Readonly<ExecutionSheetHeaderProps>) => (
  <SheetHeader className="space-y-2 border-b px-4 py-3 text-left">
    <SheetTitle className="text-base">Execution details</SheetTitle>
    {execution ? (
      <>
        <SheetDescription>
          {execution.firedUtc ? formatInZone(execution.firedUtc, tz, "datetime") : "Fired time unknown"} ({tz})
        </SheetDescription>
        <div className="flex flex-wrap items-center gap-2">
          <OutcomeBadge outcome={execution.outcome} />
          <StatusChip label={statusLabel} />
        </div>
      </>
    ) : (
      <HeaderPlaceholder loading={loading} />
    )}
  </SheetHeader>
);
