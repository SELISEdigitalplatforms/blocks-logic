import { SheetDescription, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { IScheduleExecution } from "../../../types/schedule.service.type";
import { formatInZone } from "../../../utils/timezone";
import { OutcomeBadge } from "../outcome-badge";
import { StatusChip } from "../status-chip";
import { useT } from "@seliseblocks/genesis-os";

interface ExecutionSheetHeaderProps {
  execution: IScheduleExecution | null;
  statusLabel: string;
  tz: string;
  /** With no execution: true shows a skeleton, false only the title. */
  loading?: boolean;
}

const HeaderPlaceholder = ({ loading }: Readonly<{ loading: boolean }>) => {
  const { t } = useT("logic.scheduler");
  return (
    <>
      <SheetDescription className="sr-only">{loading ? t("EXECUTION_SHEET_HEADER.LOADING_EXECUTION") : t("EXECUTION_SHEET_HEADER.EXECUTION_UNAVAILABLE")}</SheetDescription>
      {loading && (
        <>
          <Skeleton className="h-4 w-48" />
          <Skeleton className="h-6 w-40" />
        </>
      )}
    </>
  );
};

export const ExecutionSheetHeader = ({
  execution,
  statusLabel,
  tz,
  loading = false,
}: Readonly<ExecutionSheetHeaderProps>) => {
  const { t } = useT("logic.scheduler");
  return (
    <SheetHeader className="space-y-2 border-b px-4 py-3 text-left">
      <SheetTitle className="text-base">{t("EXECUTION_SHEET_HEADER.EXECUTION_DETAILS")}</SheetTitle>
      {execution ? (
        <>
          <SheetDescription>
            {execution.firedUtc ? formatInZone(execution.firedUtc, tz, "datetime") : t("EXECUTION_SHEET_HEADER.FIRED_TIME_UNKNOWN")} ({tz})
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
};
