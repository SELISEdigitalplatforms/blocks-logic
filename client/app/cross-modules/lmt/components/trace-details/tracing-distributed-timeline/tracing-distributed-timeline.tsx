import { useContext, useEffect, useMemo, useRef, useState } from "react";

import {
  Tooltip,
  TooltipContent,
  TooltipProvider,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { cn, formatDate, parseDateString } from "@/lib/utils";

import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { timelineContext } from "../trace-details";
import { TraceTree } from "@blocks-lmt/models/trace.model";

const LoadingSkelton = () => {
  return (
    <div>
      <Skeleton className="min-h-[170px] w-full rounded-none" />
    </div>
  );
};

const TracingDistributedContent = ({ trace }: { trace: TraceTree }) => {
  const { selectedTrace } = useContext(timelineContext);

  const divRef = useRef<HTMLDivElement>(null);
  const [totalWidth, setTotalWidth] = useState(0);

  useEffect(() => {
    const element = divRef.current;
    if (!element) return;

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        setTotalWidth(entry.contentRect.width);
      }
    });
    observer.observe(element);
    return () => {
      observer.disconnect();
    };
  }, [divRef]);

  // eslint-disable-next-line react-hooks/exhaustive-deps
  const getTimeLine = (item: TraceTree, unitWidth: number, startTime: string) => {
    return {
      spanId: item.spanId,
      startTime: item.startTime,
      endTime: item.endTime,
      serviceName: item.serviceName,
      duration: item.duration,
      width: item.duration * unitWidth > 1 ? item.duration * unitWidth : 1,
      marginLeft: (Number(new Date(item.startTime)) - Number(new Date(startTime))) * unitWidth,
    };
  };

  const timeLines = useMemo(() => {
    if (!trace) return [];
    const unitWidth = totalWidth / trace.duration;

    const root = getTimeLine(trace, unitWidth, trace.startTime);
    const child =
      trace?.subEntries?.map((item) => getTimeLine(item, unitWidth, trace.startTime)) || [];

    return [root, ...child];
  }, [getTimeLine, totalWidth, trace]);

  const timeSlices = useMemo(() => {
    const arr = [];
    for (let stop = 0; stop <= trace.duration; stop += trace.duration / 5) {
      arr.push(parseFloat(stop.toFixed(2)));
    }
    return arr;
  }, [trace]);

  return (
    <div className="flex min-w-full flex-col overflow-auto bg-slate-100 dark:bg-slate-900">
      <div className="mb-1 flex h-10 w-full" ref={divRef}>
        {timeSlices.slice(0, timeSlices.length - 1).map((item, index) => (
          <div
            key={index}
            className="flex h-full w-full min-w-[50px] items-center justify-between border-x border-b px-[2px] py-[4px]"
          >
            <span className="text-[12px] font-medium text-low-emphasis">{item}ms</span>
            {index == timeSlices.length - 2 && (
              <span className="text-[12px] font-medium text-low-emphasis">
                {timeSlices[timeSlices.length - 1]}ms
              </span>
            )}
          </div>
        ))}
      </div>
      <div className="min-h-[150px] pt-2">
        {timeLines.map((item) => (
          <TooltipProvider key={item.spanId}>
            <Tooltip>
              <TooltipTrigger asChild>
                <div
                  className={cn(
                    "mb-2 h-3 cursor-pointer",
                    selectedTrace?.spanId === item.spanId
                      ? "bg-chart-purple"
                      : "bg-chart-purple-light",
                  )}
                  style={{ marginLeft: item.marginLeft, width: item.width }}
                />
              </TooltipTrigger>
              <TooltipContent>
                <div className="text-xs">
                  <table>
                    <tbody>
                      <tr>
                        <td className="w-16">Service</td>
                        <td>
                          <span>{item.serviceName}</span>
                        </td>
                      </tr>
                      <tr>
                        <td>Start Time</td>
                        <td>
                          <span className="text-medium-emphasis">
                            {formatDate(parseDateString(item.startTime))}
                          </span>
                        </td>
                      </tr>
                      <tr>
                        <td>End Time</td>
                        <td>
                          <span className="text-medium-emphasis">
                            {formatDate(parseDateString(item.endTime))}
                          </span>
                        </td>
                      </tr>
                      <tr>
                        <td>Duration</td>
                        <td>
                          <span>{item.duration}</span>
                        </td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </TooltipContent>
            </Tooltip>
          </TooltipProvider>
        ))}
      </div>
    </div>
  );
};

export const TracingDistributedTimeline = () => {
  const { traceHistory, isLoading } = useContext(timelineContext);
  if (isLoading) return <LoadingSkelton />;
  if (!traceHistory.length) return <LoadingSkelton />;

  const trace = traceHistory[traceHistory?.length - 1].current;
  return (
    <>
      <TracingDistributedContent trace={trace} />
    </>
  );
};
