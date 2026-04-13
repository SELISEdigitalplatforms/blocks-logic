
import React, { useState } from "react";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui-kits/card/card";
import { abbreviateBytes, abbreviateDurationMs, abbreviateNumber } from "../../utils/usage.util";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { UsageMatrixSummary } from "../../models/usage.model";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { Info } from "lucide-react";

interface ServiceCardProps {
  isLoading: boolean;
  name: string;
  metrics: {
    api: UsageMatrixSummary;
    worker: UsageMatrixSummary;
  };
}

const UsageServiceCardSkelton = ({ name }: { name: string }) => (
  <Card className="border shadow-none transition-shadow duration-200 hover:shadow-md">
    <CardHeader className="flex flex-row items-center justify-between">
      <CardTitle className="text-lg font-semibold text-high-emphasis">{name}</CardTitle>
    </CardHeader>

    <CardContent>
      <Skeleton className="h-16 w-full" />
      <Skeleton className="mt-2 h-16 w-full" />
      <div className="mt-4 grid gap-1.5">
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Calls/min</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Peak Response</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
        <div className="flex items-center justify-between">
          <span className="text-sm text-medium-emphasis">Throughput</span>
          <Skeleton className="h-4 w-1/2" />
        </div>
      </div>
    </CardContent>
  </Card>
);

export const UsageServiceCard: React.FC<ServiceCardProps> = ({ name, metrics, isLoading }) => {
  const [selected, setSelected] = useState<"api" | "worker">("api");

  if (isLoading) return <UsageServiceCardSkelton name={name} />;

  const currentMatrix = metrics[selected];

  return (
    <Card className="border shadow-none transition-shadow duration-200 hover:shadow-md">
      <CardHeader className="flex flex-row items-center justify-between">
        <CardTitle className="text-lg font-semibold text-high-emphasis">{name}</CardTitle>
        <Tabs value={selected} onValueChange={(e) => setSelected(e as "api" | "worker")}>
          <TabsList className="h-fit bg-transparent p-0">
            <TabsTrigger
              value="api"
              className="box-border border border-transparent data-[state=active]:border-border"
            >
              API
            </TabsTrigger>
            <TabsTrigger
              value="worker"
              className="box-border border border-transparent data-[state=active]:border-border"
            >
              Worker
            </TabsTrigger>
          </TabsList>
        </Tabs>
      </CardHeader>

      <CardContent>
        <div className="flex h-16 flex-col justify-center rounded-sm bg-surface-app px-3 py-2">
          <div className="flex items-center justify-between text-high-emphasis">
            <h3 className="text-lg font-normal">API calls</h3>
            <h3 className="text-xl font-semibold">
              {abbreviateNumber(currentMatrix.TotalRequests)}
            </h3>
          </div>
          {selected === "api" && (
            <div className="mt-1 flex items-center gap-2 text-sm font-medium text-medium-emphasis">
              <div>
                Success:{" "}
                <span className="text-green-700">
                  {abbreviateNumber(currentMatrix.totalSuccess)} ({currentMatrix.successRate}%)
                </span>
              </div>
              <div className="aspect-square w-1 rounded-full bg-blocks-primary-50"></div>
              <div>
                Error:{" "}
                <span className="text-red-700">
                  {abbreviateNumber(currentMatrix.totalError)} ({currentMatrix.errorRate}%)
                </span>
              </div>
              <div className="aspect-square w-1 rounded-full bg-blocks-primary-50"></div>
              <Tooltip>
                <TooltipTrigger>
                  <Info className="aspect-square w-4" />
                </TooltipTrigger>
                <TooltipContent className="">
                  <div className="grid grid-cols-1 gap-4 md:grid-cols-2 md:gap-10">
                    <div className="flex flex-col gap-1">
                      <h4>Success Series</h4>
                      <div className="flex items-center justify-between">
                        <span>1xx</span>
                        <span>{abbreviateNumber(currentMatrix.Status1xx)}</span>
                      </div>
                      <div className="flex items-center justify-between">
                        <span>2xx</span>
                        <span>{abbreviateNumber(currentMatrix.Status2xx)}</span>
                      </div>
                      <div className="flex items-center justify-between">
                        <span>3xx</span>
                        <span>{abbreviateNumber(currentMatrix.Status3xx)}</span>
                      </div>
                    </div>
                    <div className="flex flex-col gap-1">
                      <h4>Error Series</h4>
                      <div className="flex items-center justify-between">
                        <span>4xx</span>
                        <span>{abbreviateNumber(currentMatrix.Status4xx)}</span>
                      </div>
                      <div className="flex items-center justify-between">
                        <span>5xx</span>
                        <span>{abbreviateNumber(currentMatrix.Status5xx)}</span>
                      </div>
                      <div></div>
                    </div>
                  </div>
                </TooltipContent>
              </Tooltip>
            </div>
          )}
        </div>

        <div className="mt-3 flex h-16 items-center justify-between rounded-sm bg-surface-app px-3 py-2">
          <h3 className="text-lg font-normal">Average duration</h3>
          <h3 className="text-xl font-semibold">
            {abbreviateDurationMs(currentMatrix.AverageDuration)}
          </h3>
        </div>

        <div className="mt-4 grid gap-1.5">
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Calls/min</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {currentMatrix.callsPerMinute}
            </span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Peak Response</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {abbreviateDurationMs(currentMatrix.PeakDuration)}
            </span>
          </div>
          <div className="flex items-center justify-between">
            <span className="text-sm text-medium-emphasis">Throughput</span>
            <span className="text-sm font-semibold text-high-emphasis">
              {abbreviateBytes(currentMatrix.TotalThroughput || 0)}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  );
};
