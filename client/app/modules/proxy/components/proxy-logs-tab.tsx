import { useEffect, useState } from "react";
import { Activity, Check, Copy, Loader2, Pause, Play } from "lucide-react";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Button } from "@/components/ui-kits/button/button";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Pagination } from "@/components/ui-kits/pagination/pagination";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useCopyToClipboard } from "@/hooks/use-copy-to-clipboard";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { cn } from "@/lib/utils";
import { PROXY_LOG_PAGE_SIZE, PROXY_LOG_PAGE_SIZE_OPTIONS } from "../constants";
import { useGetProxyExecution, useGetProxyExecutions } from "../hooks";
import { Proxy, ProxyExecutionLog, ProxyLogFilter } from "../types";
import { buildProxyCurl } from "../utils";
import { ProxyMethodBadge } from "./proxy-method-badge";

const FILTERS: { value: ProxyLogFilter; label: string }[] = [
  { value: "all", label: "All" },
  { value: "ok", label: "2xx" },
  { value: "client", label: "4xx" },
  { value: "server", label: "5xx" },
];

const statusClass = (status: number) =>
  status >= 500 ? "text-red-700" : status >= 400 ? "text-amber-700" : "text-green-700";

/** Human label for a server `outcome` enum, used when the row carries no `errorMessage`. */
const OUTCOME_LABELS: Record<string, string> = {
  Timeout: "The upstream endpoint did not respond in time.",
  UpstreamUnreachable: "The upstream endpoint could not be reached.",
  UpstreamBlocked: "The upstream endpoint is not an allowed destination.",
  UpstreamResponseTooLarge: "The upstream response exceeded the 10 MB limit.",
  RequestTooLarge: "The request body exceeded the 10 MB limit.",
  RequestBodyNotMergeable: "The request body is not a JSON object and could not be merged.",
  VariableResolutionFailed: "A configured configuration variable could not be resolved.",
  ResponseFilterFailed: "The upstream response could not be filtered to the configured fields.",
  MethodNotAllowed: "This HTTP method is not allowed for this proxy.",
  ProxyNotFound: "No enabled proxy is configured for this path.",
  Unauthorized: "Missing or invalid credentials for this tenant.",
  InternalError: "The proxy forwarder encountered an unexpected error.",
};

const outcomeLabel = (outcome?: string) =>
  outcome && outcome !== "Success" ? (OUTCOME_LABELS[outcome] ?? outcome) : "";

/** Pretty-print a JSON body; return the text unchanged when it is not JSON. */
const formatResponseBody = (body: string, contentType?: string) => {
  const trimmed = body.trim();
  const looksJson =
    (contentType?.toLowerCase().includes("json") ?? false) ||
    trimmed.startsWith("{") ||
    trimmed.startsWith("[");
  if (!looksJson) return body;
  try {
    return JSON.stringify(JSON.parse(trimmed), null, 2);
  } catch {
    return body;
  }
};

const logSkeletonClass = "bg-slate-200 dark:bg-muted";
const logTableGridClass = "grid-cols-[170px_96px_minmax(300px,1fr)_96px_112px]";

const ProxyLogsSkeleton = () => (
  <div className="space-y-4" role="status" aria-label="Loading request logs">
    <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="flex gap-2">
        <Skeleton className={cn("h-8 w-12", logSkeletonClass)} />
        <Skeleton className={cn("h-8 w-14", logSkeletonClass)} />
        <Skeleton className={cn("h-8 w-14", logSkeletonClass)} />
        <Skeleton className={cn("h-8 w-14", logSkeletonClass)} />
      </div>
      <div className="flex gap-2">
        <Skeleton className={cn("h-8 w-32", logSkeletonClass)} />
        <Skeleton className={cn("h-8 w-20", logSkeletonClass)} />
        <Skeleton className={cn("h-8 w-28", logSkeletonClass)} />
      </div>
    </div>
    <div className="overflow-hidden rounded-sm border">
      <div className={cn("grid gap-4 px-4 py-3", logTableGridClass)}>
        {Array.from({ length: 5 }).map((_, index) => (
          <Skeleton key={index} className={cn("h-4 w-full", logSkeletonClass)} />
        ))}
      </div>
      {Array.from({ length: 5 }).map((_, rowIndex) => (
        <div key={rowIndex} className={cn("grid gap-4 border-t px-4 py-3", logTableGridClass)}>
          {Array.from({ length: 5 }).map((_, cellIndex) => (
            <Skeleton key={cellIndex} className={cn("h-4 w-full", logSkeletonClass)} />
          ))}
        </div>
      ))}
    </div>
  </div>
);

/**
 * One labelled copy action. Swaps to a tick for the duration of the hook's cooldown so a click on a
 * value that looks identical to the last one still reads as having done something.
 */
const CopyButton = ({
  label,
  value,
  title,
}: {
  label: string;
  value: string;
  title: string;
}) => {
  const { isCopying, copy } = useCopyToClipboard();

  return (
    <Button
      type="button"
      variant="outline"
      size="sm"
      className="h-7 gap-1.5 px-2 text-xs font-medium"
      title={title}
      aria-label={title}
      onClick={() =>
        copy(
          value,
          () => showSuccessToast({ description: `${label} copied to clipboard.` }),
          () => showErrorToast({ errors: `Could not copy the ${label.toLowerCase()}.` }),
        )
      }
    >
      {isCopying ? (
        <Check className="h-3.5 w-3.5 text-green-600" />
      ) : (
        <Copy className="h-3.5 w-3.5" />
      )}
      {label}
    </Button>
  );
};

const LogDetails = ({ proxyId, log }: { proxyId: string; log: ProxyExecutionLog }) => {
  // The list row carries only summary fields; the upstream response body, forwarded
  // URL and injected keys are fetched on demand from `GET /api/Proxies/{proxyId}/executions/{executionId}`.
  const { data, isLoading, isError } = useGetProxyExecution(proxyId, log.id);
  const detail = data ?? log;
  const errorText = detail.errorMessage || outcomeLabel(detail.outcome);
  const showError = detail.status >= 400 && Boolean(errorText);

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 border-t bg-muted/20 px-4 py-6 text-sm text-muted-foreground">
        <Loader2 className="h-4 w-4 animate-spin" />
        Loading response...
      </div>
    );
  }

  const curl = buildProxyCurl(
    detail,
    typeof window === "undefined" ? "" : window.location.origin,
  );

  return (
    <div className="grid gap-3 border-t bg-muted/20 px-4 py-3 text-sm lg:grid-cols-2">
      <div className="flex flex-wrap items-center justify-end gap-2 lg:col-span-2">
        <CopyButton
          label="cURL"
          value={curl}
          title="Copy this call as a curl command (credentials left as placeholders)"
        />
        <CopyButton
          label="JSON"
          value={JSON.stringify(detail, null, 2)}
          title="Copy the whole log row as JSON"
        />
      </div>
      <div>
        <span className="text-xs font-medium uppercase text-muted-foreground">Forwarded to</span>
        <div className="flex items-start gap-2">
          <p className="break-all font-mono">{detail.upstreamUrl || detail.upstreamHost || "—"}</p>
          {detail.upstreamUrl || detail.upstreamHost ? (
            <CopyButton
              label="URL"
              value={detail.upstreamUrl || detail.upstreamHost}
              title="Copy the forwarded upstream URL"
            />
          ) : null}
        </div>
      </div>
      <div>
        <span className="text-xs font-medium uppercase text-muted-foreground">Result</span>
        <p>
          {detail.status} {detail.statusText} in {detail.latencyMs}ms
        </p>
      </div>
      <div>
        <span className="text-xs font-medium uppercase text-muted-foreground">
          Injected credentials
        </span>
        <p>{[...detail.injectedHeaderKeys, ...detail.injectedQueryKeys].join(", ") || "None"}</p>
      </div>
      {showError ? (
        <div className="lg:col-span-2">
          <span className="text-xs font-medium uppercase text-muted-foreground">Error</span>
          <p className="mt-1 text-red-700">{errorText}</p>
        </div>
      ) : null}
      <div className="lg:col-span-2">
        <div className="flex items-center justify-between gap-2">
          <span className="text-xs font-medium uppercase text-muted-foreground">Response body</span>
          {detail.responseBody ? (
            <CopyButton
              label="Body"
              value={detail.responseBody}
              title="Copy the stored response body"
            />
          ) : null}
        </div>
        {isError ? (
          <p className="mt-1 text-red-700">Failed to load the response body.</p>
        ) : (
          <pre className="mt-1 max-h-96 overflow-auto whitespace-pre-wrap break-words rounded-sm bg-background p-3 text-xs">
            {detail.responseBody
              ? formatResponseBody(detail.responseBody, detail.responseContentType)
              : "(empty response body)"}
          </pre>
        )}
      </div>
    </div>
  );
};

export const ProxyLogsTab = ({ proxy, active }: { proxy: Proxy; active: boolean }) => {
  const [filter, setFilter] = useState<ProxyLogFilter>("all");
  const [live, setLive] = useState(false);
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState<number>(PROXY_LOG_PAGE_SIZE);
  /**
   * Window top for the current paging session, pinned from the first response and sent with every
   * later page. Cleared whenever the result set is redefined (filter, page size, Live) so the next
   * fetch re-pins to "now" — a session only needs to be stable while the reader pages through it.
   */
  const [asOfUtc, setAsOfUtc] = useState<string | undefined>(undefined);
  const {
    data: logPage,
    isFetching,
    isLoading,
  } = useGetProxyExecutions(proxy.id, filter, {
    live: active && live && proxy.enabled,
    enabled: active,
    page,
    pageSize,
    // Live mode is cursor-based and always wants the newest rows, so it never pins.
    asOfUtc: live ? undefined : asOfUtc,
  });
  const data = logPage?.rows ?? [];
  const totalCount = logPage?.totalCount ?? 0;
  const servedAsOf = logPage?.asOfUtc;

  useEffect(() => {
    if (!live && !asOfUtc && servedAsOf) setAsOfUtc(servedAsOf);
  }, [live, asOfUtc, servedAsOf]);
  const {
    data: allRowsPage,
    isFetched: hasFetchedAllRows,
    isLoading: isLoadingAllRows,
  } = useGetProxyExecutions(proxy.id, "all", { enabled: active, pageSize: 1 });
  const totalAllCount = allRowsPage?.totalCount ?? 0;

  const handleFilter = (next: ProxyLogFilter) => {
    setFilter(next);
    setExpandedId(null);
    setPage(0);
    setAsOfUtc(undefined);
  };

  const handlePageSizeChange = (next: number) => {
    setPageSize(next);
    setPage(0);
    setAsOfUtc(undefined);
  };

  if (active && (isLoading || isLoadingAllRows)) {
    return <ProxyLogsSkeleton />;
  }

  if (active && hasFetchedAllRows && totalAllCount === 0) {
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
            {data.length} of {totalAllCount} requests
          </span>
          <Button
            variant="outline"
            size="xs"
            className="gap-1.5"
            onClick={() => {
              // Leaving Live drops back into a fresh paging session rather than one pinned to
              // whenever the tab was first opened.
              setLive((value) => !value);
              setAsOfUtc(undefined);
              setPage(0);
            }}
          >
            {live ? <Pause className="h-3.5 w-3.5" /> : <Play className="h-3.5 w-3.5" />}
            {live ? "Paused" : "Live"}
          </Button>
        </div>
      </div>
      <div className="overflow-x-auto rounded-sm border bg-card">
        <table className="min-w-[900px] w-full text-sm">
          <thead className="bg-muted/40 text-xs text-muted-foreground">
            <tr>
              <th colSpan={5} className="p-0 text-left font-medium">
                <div className={cn("grid gap-4 px-4 py-2", logTableGridClass)}>
                  <span role="columnheader">TIME</span>
                  <span role="columnheader">METHOD</span>
                  <span role="columnheader">PATH</span>
                  <span role="columnheader">STATUS</span>
                  <span role="columnheader">LATENCY</span>
                </div>
              </th>
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
                      className={cn("grid w-full gap-4 px-4 py-3 text-left", logTableGridClass)}
                      onClick={() => setExpandedId((id) => (id === log.id ? null : log.id))}
                    >
                      <span>{new Date(log.timeUtc).toLocaleTimeString()}</span>
                      <ProxyMethodBadge method={log.method} />
                      <span className="truncate font-mono text-xs">{log.path}</span>
                      <Badge variant="outline" className={cn("w-fit", statusClass(log.status))}>
                        {log.status}
                      </Badge>
                      <span>{log.latencyMs}ms</span>
                    </button>
                    {expandedId === log.id ? <LogDetails proxyId={proxy.id} log={log} /> : null}
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
      {totalCount > 0 ? (
        <div className="flex justify-end">
          <Pagination
            totalCount={totalCount}
            page={page}
            pageSize={pageSize}
            pageSizeOptions={[...PROXY_LOG_PAGE_SIZE_OPTIONS]}
            onChange={setPage}
            onPageSizeChange={handlePageSizeChange}
          />
        </div>
      ) : null}
      {isFetching ? <p className="text-xs text-muted-foreground">Refreshing logs...</p> : null}
    </div>
  );
};
