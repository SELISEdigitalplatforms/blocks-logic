import React, { createContext, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import PageBreadcrumb from "@/components/breadcrumb/breadcrumb";
import { BREADCRUMB_CUSTOM_TITLES } from "@/constants/breadcrumb-custom-title";
import { ArrowLeft, Download, PanelRightClose, PanelRightOpen } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import useIsMobile from "@/hooks/use-is-mobile";

import { useProjectStore } from "@/store/useProjectStore";
import { useGetTraceById } from "@blocks-lmt/hooks/use-trace";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { TracingListBreadCrumb } from "./tracing-list-breadcrum/tracing-list-breadcrum";
import { TracingInsights } from "./tracing-insights/tracing-insights";
import { TracingDistributedTimeline } from "./tracing-distributed-timeline/tracing-distributed-timeline";
import { ActivityLogs } from "./activity-logs/activity-logs";
import { TraceTree } from "@blocks-lmt/models/trace.model";

export const timelineContext = createContext<{
  traceHistory: {
    rootId: string;
    current: TraceTree;
    root: TraceTree;
  }[];
  setTraceHistory: React.Dispatch<
    React.SetStateAction<
      {
        rootId: string;
        current: TraceTree;
        root: TraceTree;
      }[]
    >
  >;
  selectedTrace: TraceTree | null;
  setSelectedTrace: React.Dispatch<React.SetStateAction<TraceTree | null>>;
  isPanelOpen: boolean;
  isLoading: boolean;
}>({
  traceHistory: [],
  setTraceHistory: () => {},
  selectedTrace: null,
  setSelectedTrace: () => {},
  isPanelOpen: true,
  isLoading: false,
});

export const TraceDetails = ({ id }: { id: string }) => {
  const navigate = useNavigate();
  const isMobile = useIsMobile();
  const [isPanelOpen, setIsPanelOpen] = useState(true);
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [traceHistory, setTraceHistory] = useState<
    {
      rootId: string;
      current: TraceTree;
      root: TraceTree;
    }[]
  >([]);

  const { isLoading, isFetching, data } = useGetTraceById({ traceId: id, projectKey: tenantId });

  const [selectedTrace, setSelectedTrace] = useState<TraceTree | null>(null);

  useEffect(() => {
    if (data?.data) {
      const trace = data.data;
      setTraceHistory([
        {
          root: trace,
          current: trace,
          rootId: trace.spanId,
        },
      ]);
      setSelectedTrace(trace);
    }
  }, [data?.data]);

  const downloadJSONFile = () => {
    if (!data?.data) return;

    const jsonString = JSON.stringify(data.data, null, 2);
    const blob = new Blob([jsonString], { type: "application/json" });
    const link = document.createElement("a");
    link.href = URL.createObjectURL(blob);
    link.download = `tracing-details-${data.data.traceId}.json`;

    document.body.appendChild(link);
    link.click();

    document.body.removeChild(link);
    URL.revokeObjectURL(link.href);
  };

  BREADCRUMB_CUSTOM_TITLES["/tracing/timeline"] = "Tracing";

  const selectedTraceHistory = traceHistory[traceHistory?.length - 1];

  return (
    <timelineContext.Provider
      value={{
        traceHistory,
        setTraceHistory,
        selectedTrace,
        setSelectedTrace,
        isPanelOpen,
        isLoading: isLoading || isFetching,
      }}
    >
      {isLoading || isFetching ? (
        <Skeleton className="h-8 w-40" />
      ) : (
        <div className="hidden md:flex">
          <PageBreadcrumb breadcrumbIndex={2} />
        </div>
      )}
      <div className="flex items-center justify-between md:py-[24px]">
        {isLoading || isFetching ? (
          <Skeleton className="h-8 w-1/3" />
        ) : (
          <div className="flex items-center">
            <Button size="icon" variant="ghost" className="h-8 w-8" onClick={() => navigate(-1)}>
              <ArrowLeft className="h-6 w-6" />
            </Button>
            <h1 className="ml-2 break-all text-lg font-semibold md:ml-4 md:text-2xl">
              {data?.data?.entryPoint?.method}{" "}
              <span className="text-low-emphasis">{data?.data?.entryPoint?.actionName}</span>
            </h1>
          </div>
        )}
        {isLoading || isFetching ? (
          <Skeleton className="h-8 w-40" />
        ) : (
          <div className="ml-auto flex items-center gap-2">
            <Button
              size="default"
              variant="outline"
              className="shadow-none"
              onClick={downloadJSONFile}
            >
              <Download className="h-4 w-4 lg:mr-2" />
              <span className="sr-only lg:not-sr-only">Download JSON</span>
            </Button>
          </div>
        )}
      </div>
      <div className="mt-6 flex w-full flex-col gap-6 md:flex-row">
        <div className={`${isPanelOpen ? "w-full md:w-[68%]" : "w-full"}`}>
          <Card className="h-min rounded-sm shadow-none">
            <CardHeader>
              <div className="flex flex-col items-start justify-between sm:flex-row sm:items-center">
                <CardTitle className="text-xl">
                  {isLoading || isFetching ? <Skeleton className="h-6 w-28" /> : "Timeline"}
                </CardTitle>

                {isLoading || isFetching ? (
                  <Skeleton className="h-6 w-64" />
                ) : (
                  <div
                    className={`mt-2 flex flex-col items-start gap-2 md:mt-0 md:flex-row md:items-center md:justify-end md:gap-10`}
                  >
                    <div className="flex flex-col items-start text-[12px] font-medium text-low-emphasis sm:items-center lg:flex-row">
                      <span>Duration </span>
                      <span className="ml-1 break-all text-high-emphasis">
                        {selectedTraceHistory?.current?.duration.toFixed(2)}ms
                      </span>
                    </div>
                    <div className="flex flex-col items-start text-[12px] font-medium text-low-emphasis sm:items-center lg:flex-row">
                      <span>Trace ID </span>
                      <span className="ml-1 break-all text-high-emphasis">
                        {data?.data?.traceId}
                      </span>
                    </div>
                  </div>
                )}
              </div>

              <div></div>
              <TracingListBreadCrumb />
            </CardHeader>
            <CardContent className="text-base">
              <div className="flex flex-col">
                <div className="mb-[16px]">
                  <TracingDistributedTimeline />
                </div>
                <div className="mb-[16px] flex items-center justify-end">
                  {!isMobile && (
                    <Button
                      variant="outline"
                      className="h-10 w-10 p-0"
                      onClick={() => setIsPanelOpen(!isPanelOpen)}
                    >
                      {isPanelOpen ? (
                        <PanelRightClose width={20} height={20} />
                      ) : (
                        <PanelRightOpen width={20} height={20} />
                      )}
                    </Button>
                  )}
                </div>
                <ActivityLogs />
              </div>
            </CardContent>
          </Card>
        </div>
        {isPanelOpen && (
          <div
            className={`w-full overflow-hidden transition-all duration-500 ease-in-out md:w-[32%] ${
              isPanelOpen ? "translate-x-0 opacity-100" : "translate-x-full opacity-0"
            }`}
          >
            <TracingInsights />
          </div>
        )}
      </div>
    </timelineContext.Provider>
  );
};

export default TraceDetails;
