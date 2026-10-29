import { useMemo } from "react";
import { Sheet, SheetContent } from "@/components/ui-kits/sheet/sheet";
import { useGetScheduleExecution } from "../../../hooks/use-schedule-api";
import { IScheduleExecution } from "../../../types/schedule.service.type";
import { ParsedSnapshot, parseSnapshot } from "../parse-snapshot";
import { sheetStatusLabel } from "../status-label";
import { ExecutionOverviewSection } from "./execution-overview-section";
import { ExecutionRawSnapshotSection } from "./execution-raw-snapshot-section";
import { ExecutionRequestSection } from "./execution-request-section";
import { ExecutionResponseSection } from "./execution-response-section";
import { ExecutionScheduleSection } from "./execution-schedule-section";
import { ExecutionSheetHeader } from "./execution-sheet-header";
import { ExecutionSheetError, ExecutionSheetLoading, SnapshotUnavailable } from "./execution-sheet-states";

interface ScheduleExecutionSheetProps {
  scheduleId: string;
  /** The open execution; null closes the sheet. */
  executionId: string | null;
  tz: string;
  currentName?: string | null;
  onClose: () => void;
}

const SnapshotSections = ({ parsed }: Readonly<{ parsed: ParsedSnapshot }>) => {
  if (parsed.kind === "none") return <SnapshotUnavailable />;
  if (parsed.kind === "invalid") {
    return (
      <>
        <SnapshotUnavailable />
        <ExecutionRawSnapshotSection value={parsed.raw} invalid />
      </>
    );
  }
  return (
    <>
      <ExecutionRequestSection snapshot={parsed.snapshot} />
      <ExecutionScheduleSection snapshot={parsed.snapshot} />
      <ExecutionRawSnapshotSection value={parsed.pretty} />
    </>
  );
};

interface ExecutionBodyProps {
  execution: IScheduleExecution;
  tz: string;
  currentName?: string | null;
}

const ExecutionBody = ({ execution, tz, currentName }: Readonly<ExecutionBodyProps>) => {
  const parsed = useMemo(() => parseSnapshot(execution.snapshot), [execution.snapshot]);
  const triggerType = parsed.kind === "ok" ? parsed.snapshot.triggerType : null;
  const label = sheetStatusLabel(execution.status, triggerType);

  return (
    <>
      <ExecutionSheetHeader execution={execution} statusLabel={label} tz={tz} />
      <div className="space-y-6 px-4 py-4">
        <ExecutionOverviewSection execution={execution} statusLabel={label} tz={tz} currentName={currentName} />
        <ExecutionResponseSection body={execution.body} statusLabel={label} />
        <SnapshotSections parsed={parsed} />
      </div>
    </>
  );
};

const SheetBody = ({ scheduleId, executionId, tz, currentName }: Readonly<Omit<ScheduleExecutionSheetProps, "onClose">>) => {
  const { data, isLoading, isError, refetch } = useGetScheduleExecution(scheduleId, executionId ?? "", {
    enabled: !!executionId,
  });

  if (isLoading) {
    return (
      <>
        <ExecutionSheetHeader execution={null} statusLabel="" tz={tz} loading />
        <ExecutionSheetLoading />
      </>
    );
  }

  if (isError || !data?.isSuccess || !data.data) {
    return (
      <>
        <ExecutionSheetHeader execution={null} statusLabel="" tz={tz} />
        <ExecutionSheetError notFound={!!data?.errors?.not_found} onRetry={() => void refetch()} />
      </>
    );
  }

  return <ExecutionBody execution={data.data} tz={tz} currentName={currentName} />;
};

/** The right-side details sheet for one run; modal, so Esc closes it and focus returns to the row. */
export const ScheduleExecutionSheet = ({ onClose, ...props }: Readonly<ScheduleExecutionSheetProps>) => (
  <Sheet open={!!props.executionId} onOpenChange={(open) => !open && onClose()}>
    <SheetContent side="right" className="w-full overflow-y-auto p-0 sm:max-w-2xl">
      {props.executionId && <SheetBody {...props} />}
    </SheetContent>
  </Sheet>
);
