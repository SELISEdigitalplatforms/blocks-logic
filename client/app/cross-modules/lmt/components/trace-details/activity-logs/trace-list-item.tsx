import { ChevronDown } from "lucide-react";
import { useContext } from "react";
import { timelineContext } from "../trace-details";
import { TraceTree } from "@blocks-lmt/models/trace.model";

type TraceListItemProps = { trace: TraceTree };

export const TraceListItem = ({ trace }: TraceListItemProps) => {
  const { setTraceHistory, setSelectedTrace, selectedTrace } = useContext(timelineContext);
  const setTraceHistoryHandler = () => {
    setTraceHistory((prev) => {
      return [...prev, { rootId: trace.spanId, root: trace, current: trace.subEntries[0] }];
    });
    setSelectedTrace(() => trace.subEntries[0]);
  };
  const isSelected = trace.spanId === selectedTrace?.spanId;
  return (
    <div
      className="space-y-1"
      onClick={() => {
        setSelectedTrace(trace);
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
        {trace?.subEntries?.length ? (
          <div
            onClick={(e) => {
              e.stopPropagation();
              setTraceHistoryHandler();
            }}
            className="rounded-sm bg-white p-2 dark:bg-gray-700"
          >
            <ChevronDown className="h-4 w-4" />
          </div>
        ) : (
          ""
        )}
      </div>
    </div>
  );
};
