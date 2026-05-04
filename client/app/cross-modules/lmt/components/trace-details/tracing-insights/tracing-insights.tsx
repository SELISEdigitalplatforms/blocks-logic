import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui-kits/tabs/tabs";
import { useContext, useState } from "react";
import { TracingInfo } from "./tracing-info";
import { TracingLog } from "./tracing-log";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { timelineContext } from "../trace-details";

const LoadingSkelton = () => {
  return (
    <Card>
      <CardContent>
        <Skeleton className="h-8 w-32" />
        <div className="mt-4">
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="h-6 w-full" />
        </div>
        <div className="mt-8">
          <div className="flex items-center justify-between">
            <Skeleton className="mb-2 h-6 w-32" />
            <Skeleton className="mb-2 h-6 w-24" />
          </div>
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="h-6 w-full" />
        </div>
        <div className="mt-8">
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
          <Skeleton className="mb-2 h-6 w-full" />
        </div>
      </CardContent>
    </Card>
  );
};

export const TracingInsights = () => {
  const { isLoading, traceHistory } = useContext(timelineContext);
  const [tabId, setTabId] = useState("info");
  const tabChangeHandler = (value: string) => setTabId(value);

  if (isLoading || !traceHistory?.length) return <LoadingSkelton />;

  return (
    <Card className="rounded-sm shadow-none">
      <CardContent className="flex flex-col">
        <Tabs value={tabId}>
          <div className="mb-5 flex items-center rounded text-base">
            <TabsList>
              <TabsTrigger onClick={() => tabChangeHandler("info")} value="info">
                Info
              </TabsTrigger>

              <TabsTrigger
                onClick={() => tabChangeHandler("log")}
                value="log"
                className="disabled:pointer-events-auto disabled:cursor-not-allowed"
              >
                Log
              </TabsTrigger>
            </TabsList>
          </div>
          <TabsContent value="info">
            <TracingInfo />
          </TabsContent>
          <TabsContent value="log">
            <TracingLog />
          </TabsContent>
        </Tabs>
      </CardContent>
    </Card>
  );
};
