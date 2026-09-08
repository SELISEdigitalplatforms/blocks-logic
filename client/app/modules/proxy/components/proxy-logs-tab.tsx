import { useState } from "react";
import { Activity, Download, Pause, Play } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { useExportProxyExecutionCsv, useGetProxyExecutions } from "../hooks";
import { Proxy, ProxyExecutionLog, ProxyLogFilter } from "../types";

const FILTERS: { value: ProxyLogFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "ok", label: "2xx" },
  { value: "client", label: "4xx" },
  { value: "server", label: "5xx" },
];

const statusClass = (status: number) =>
  status >= 500 ? "text-red-700" : status >= 400 ? "text-amber-700" : "text-green-700";

const ProxyLogsSkeleton = () => (
  <div className="space-y-4">
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex gap-2">
        <Skeleton className="h-8 w-12" />
        <Skeleton className="h-8 w-14" />
        <Skeleton className="h-8 w-14" />
        <Skeleton className="h-8 w-14" />
      </div>
      <div className="flex gap-2">
        <Skeleton className="h-8 w-32" />
        <Skeleton className="h-8 w-20" />
        <Skeleton className="h-8 w-28" />
      </div>
    </div>
    <div className="overflow-hidden rounded-sm border">
      <div className="grid grid-cols-[150px_80px_minmax(160px,1fr)_80px_80px] gap-4 px-4 py-3">
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className="h-4 w-full" />
        ))}
      </div>
      {Array.from({ length: 5 }).map((_, rowIndex) => (
        <div
          key={rowIndex}
          className="grid grid-cols-[150px_80px_minmax(160px,1fr)_80px_80px] gap-4 border-t px-4 py-3"
        >
          {Array.from({ length: 5 }).map((_, cellIndex) => (
            <Skeleton key={cellIndex} className="h-4 w-full" />
          ))}
        </div>
      ))}
    </div>
  </div>
);

const LogDetails = ({ log }: { log: ProxyExecutionLog }) => (
  <div className="grid gap-3 border-t bg-muted/20 px-4 py-3 text-sm lg:grid-cols-2">
    <div>
      <span className="text-xs font-medium uppercase text-muted-foreground">Forwarded to</span>
      <p className="break-all font-mono">{log.upstreamUrl}</p>
    </div>
    <div>
      <span className="text-xs font-medium uppercase text-muted-foreground">Result</span>
      <p>
        {log.status} {log.statusText} in {log.latencyMs}ms
      </p>
    </div>
    <div>
      <span className="text-xs font-medium uppercase text-muted-foreground">
        Injected credentials
      </span>
      <p>{[...log.injectedHeaderKeys, ...log.injectedQueryKeys].join(", ") || "None"}</p>
    </div>
    <pre className="max-h-48 overflow-auto rounded-sm bg-background p-3 text-xs lg:col-span-2">
      {log.responseBody}
    </pre>
  </div>
);

export const ProxyLogsTab = ({ proxy, active }: { proxy: Proxy; active: boolean }) => {
  const [filter, setFilter] = useState<ProxyLogFilter>("all");
  const [live, setLive] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const {
    data = [],
    isFetching,
    isLoading,
  } = useGetProxyExecutions(proxy.id, filter, {
    live: active && live && proxy.enabled,
    enabled: active,
  });
  const {
    data: allRows = [],
    isFetched: hasFetchedAllRows,
    isLoading: isLoadingAllRows,
  } = useGetProxyExecutions(proxy.id, "all", { enabled: active });
  const exportCsv = useExportProxyExecutionCsv();

  const handleFilter = (next: ProxyLogFilter) => {
    setFilter(next);
    setExpandedId(null);
  };

  const handleExport = async () => {
    try {
      const res = await exportCsv.mutateAsync({ proxyId: proxy.id, filter });
      const blob = new Blob([res.csv], { type: "text/csv;charset=utf-8" });
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = res.fileName;
      link.click();
      URL.revokeObjectURL(url);
      showSuccessToast({ description: `${res.rowCount} requests exported as CSV.` });
    } catch {
      showErrorToast({ errors: "Failed to export proxy requests." });
    }
  };

  if (active && (isLoading || isLoadingAllRows)) {
    return <ProxyLogsSkeleton />;
  }

  if (active && hasFetchedAllRows && !allRows.length) {
    return (
      <Card className="p-0">
        <CardContent className="flex min-h-[260px] flex-col items-center justify-center px-6 py-12 text-center">
          <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
            <Activity className="h-7 w-7" />
          </div>
          <h3 className="mt-5 text-xl font-semibold">No request logs yet</h3>
          <p className="mt-2 max-w-md text-sm text-muted-foreground">
            Requests sent through this proxy will appear here once traffic starts flowing.
          </p>
        </CardContent>
      </Card>
    );
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex flex-wrap gap-2">
          {FILTERS.map((item) => (
            <Button
              key={item.value}
              type="button"
              variant={filter === item.value ? "default" : "outline"}
              size="xs"
              onClick={() => handleFilter(item.value)}
            >
              {item.label}
            </Button>
          ))}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-sm text-muted-foreground">
            {data.length} of {allRows.length} requests
          </span>
          <Button
            variant="outline"
            size="xs"
            className="gap-1.5"
            onClick={() => setLive((value) => !value)}
          >
            {live ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
            {live ? "Paused" : "Live"}
          </Button>
          <Button variant="outline" size="xs" className="gap-1.5" onClick={handleExport}>
            <Download className="h-3.5 w-3.5" />
            Export CSV
          </Button>
        </div>
      </div>
      <div className="overflow-hidden rounded-sm border">
        <table className="w-full text-sm">
          <thead className="bg-muted/40 text-xs text-muted-foreground">
            <tr>
              <th className="px-4 py-2 text-left">TIME</th>
              <th className="px-4 py-2 text-left">METH</th>
              <th className="px-4 py-2 text-left">PATH</th>
              <th className="px-4 py-2 text-left">CODE</th>
              <th className="px-4 py-2 text-left">TOOK</th>
            </tr>
          </thead>
          <tbody>
            {!data.length ? (
              <tr>
                <td colSpan={5} className="px-4 py-10 text-center text-muted-foreground">
                  No requests match this filter.
                </td>
              </tr>
            ) : (
              data.map((log) => (
                <tr key={log.id} className="border-t align-top">
                  <td colSpan={5} className="p-0">
                    <button
                      type="button"
                      className="grid w-full grid-cols-[150px_80px_minmax(160px,1fr)_80px_80px] px-4 py-3 text-left"
                      onClick={() => setExpandedId((id) => (id === log.id ? null : log.id))}
                    >
                      <span>{new Date(log.timeUtc).toLocaleTimeString()}</span>
                      <span className="font-mono">{log.method}</span>
                      <span className="truncate font-mono text-xs">{log.path}</span>
                      <Badge variant="outline" className={cn("w-fit", statusClass(log.status))}>
                        {log.status}
                      </Badge>
                      <span>{log.latencyMs}ms</span>
                    </button>
                    {expandedId === log.id ? <LogDetails log={log} /> : null}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      {isFetching ? <p className="text-xs text-muted-foreground">Refreshing logs...</p> : null}
    </div>
  );
};
