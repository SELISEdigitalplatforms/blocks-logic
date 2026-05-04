import { TraceListItem } from "./trace-list-item";
import { useContext } from "react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { timelineContext } from "../trace-details";
import { TraceTree } from "@blocks-lmt/models/trace.model";

const LoadingSkelton = () => {
  return (
    <div className="flex flex-col gap-1">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
    </div>
  );
};

const ActivityLogsContent = ({ trace }: { trace: TraceTree }) => {
  const { setSelectedTrace, selectedTrace } = useContext(timelineContext);
  const isSelected = trace.spanId === selectedTrace?.spanId;
  return (
    <>
      <div
        className="space-y-1"
        onClick={() => {
          setSelectedTrace({ ...trace });
        }}
      >
        <div
          className={`flex cursor-pointer items-center justify-between rounded-lg p-2 hover:bg-blocks-primary-shades-300 ${isSelected ? "!bg-blocks-primary-50" : ""}`}
        >
          <div className="flex items-center space-x-2">
            <span className="text-[12px] font-medium text-medium-emphasis">
              {trace.activitySourceName} {trace.operationName}
            </span>
          </div>
        </div>
      </div>
      {trace?.subEntries?.length ? (
        <div className="ml-6">
          {trace?.subEntries.map((item) => <TraceListItem key={item.spanId} trace={item} />)}
        </div>
      ) : null}
    </>
  );
};

export const ActivityLogs = () => {
  const { traceHistory, isLoading } = useContext(timelineContext);

  if (isLoading) return <LoadingSkelton />;
  if (!traceHistory.length) return <LoadingSkelton />;
  const trace = traceHistory[traceHistory?.length - 1].current;

  return <ActivityLogsContent trace={trace} />;
};
