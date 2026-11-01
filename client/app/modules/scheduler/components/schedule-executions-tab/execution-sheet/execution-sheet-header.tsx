import { Braces } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { SheetDescription, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
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

const CopyJsonButton = ({ execution }: Readonly<{ execution: IScheduleExecution }>) => {
  const { copy, isCopying } = useCopyToClipboard();
  const onCopy = () =>
    copy(
      JSON.stringify(execution, null, 2),
      () => showSuccessToast({ description: "Execution JSON copied to clipboard." }),
      () => showErrorToast({ errors: "Couldn't copy the execution JSON." }),
    );

  return (
    <Button type="button" variant="outline" size="xs" className="gap-1.5" disabled={isCopying} onClick={onCopy}>
      <Braces className="h-3.5 w-3.5" aria-hidden />
      Copy JSON
    </Button>
  );
};

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
          <div className="ml-auto">
            <CopyJsonButton execution={execution} />
          </div>
        </div>
      </>
    ) : (
      <HeaderPlaceholder loading={loading} />
    )}
  </SheetHeader>
);
