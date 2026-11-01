import { useState } from "react";
import { Activity, Check, ChevronRight, Copy, Loader2, Pause, Play } from "lucide-react";
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
import { useT, tStatic } from "@seliseblocks/genesis-os";

const FILTERS: { value: ProxyLogFilter; label: string }[] = [
  {
    value: "all",
    get label() {
      return tStatic("logic.proxy.components", "common:ALL");
    },
  },
  { value: "ok", label: "2xx" },
  { value: "client", label: "4xx" },
  { value: "server", label: "5xx" },
];

const statusClass = (status: number) =>
  status >= 500 ? "text-red-700" : status >= 400 ? "text-amber-700" : "text-green-700";

/** Human label for a server `outcome` enum, used when the row carries no `errorMessage`. */
const OUTCOME_LABELS: Record<string, string> = {
  get Timeout() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_UPSTREAM_ENDPOINT_DID");
  },
  get UpstreamUnreachable() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_UPSTREAM_ENDPOINT_COULD");
  },
  get UpstreamBlocked() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_UPSTREAM_ENDPOINT_IS");
  },
  get UpstreamResponseTooLarge() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_UPSTREAM_RESPONSE_EXCEEDED");
  },
  get RequestTooLarge() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_REQUEST_BODY_EXCEEDED");
  },
  get RequestBodyNotMergeable() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_REQUEST_BODY_IS");
  },
  get VariableResolutionFailed() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.A_CONFIGURED_CONFIGURATION_VARIABLE");
  },
  get ResponseFilterFailed() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_UPSTREAM_RESPONSE_COULD");
  },
  get MethodNotAllowed() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THIS_HTTP_METHOD_IS");
  },
  get ProxyNotFound() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.NO_ENABLED_PROXY_IS");
  },
  get Unauthorized() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.MISSING_OR_INVALID_CREDENTIALS");
  },
  get InternalError() {
    return tStatic("logic.proxy.components", "PROXY_LOGS_TAB.THE_PROXY_FORWARDER_ENCOUNTERED");
  },
};

const outcomeLabel = (outcome?: string) =>
  outcome && outcome !== "Success" ? (OUTCOME_LABELS[outcome] ?? outcome) : "";

const logSkeletonClass = "bg-slate-200 dark:bg-muted";
const logTableGridClass = "grid-cols-[20px_170px_96px_minmax(300px,1fr)_96px_112px]";

const ProxyLogsSkeleton = () => {
  const { t } = useT("logic.proxy.components");
  return (
    <div className="space-y-4" role="status" aria-label={t("PROXY_LOGS_TAB.LOADING_REQUEST_LOGS")}>
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
          {Array.from({ length: 6 }).map((_, index) => (
            <Skeleton key={index} className={cn("h-4 w-full", logSkeletonClass)} />
          ))}
        </div>
        {Array.from({ length: 5 }).map((_, rowIndex) => (
          <div key={rowIndex} className={cn("grid gap-4 border-t px-4 py-3", logTableGridClass)}>
            {Array.from({ length: 6 }).map((_, cellIndex) => (
              <Skeleton key={cellIndex} className={cn("h-4 w-full", logSkeletonClass)} />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
};

/**
 * One labelled copy action. Swaps to a tick for the duration of the hook's cooldown so a click on a
 * value that looks identical to the last one still reads as having done something.
 *
 * `iconOnly` drops the label and the outline/background, leaving a bare icon — used for the
 * hover-revealed copy affordances next to inline values (the forwarded URL, the response body).
 */
const CopyButton = ({
  label,
  value,
  title,
  iconOnly,
  className,
}: {
  label: string;
  value: string;
  title: string;
  iconOnly?: boolean;
  className?: string;
}) => {
  const { t } = useT("logic.proxy.components");
  const { isCopying, copy } = useCopyToClipboard();

  return (
    <Button
      type="button"
      variant={iconOnly ? "ghost" : "outline"}
      size={iconOnly ? "icon" : "sm"}
      className={cn(
        iconOnly ? "h-6 w-6 shrink-0" : "h-7 gap-1.5 px-2 text-xs font-medium",
        className,
      )}
      title={title}
      aria-label={title}
      onClick={() =>
        copy(
          value,
          () =>
            showSuccessToast({ description: t("PROXY_LOGS_TAB.COPIED_TO_CLIPBOARD", { label }) }),
          () =>
            showErrorToast({
              errors: t("PROXY_LOGS_TAB.COULD_NOT_COPY_THE", { value: label.toLowerCase() }),
            }),
        )
      }
    >
      {isCopying ? (
        <Check className="h-3.5 w-3.5 text-green-600" />
      ) : (
        <Copy className="h-3.5 w-3.5" />
      )}
      {!iconOnly && label}
    </Button>
  );
};

const logDetailsId = (logId: string) => `proxy-log-details-${logId}`;

const LogDetails = ({ proxyId, log }: { proxyId: string; log: ProxyExecutionLog }) => {
  const { t } = useT("logic.proxy.components");
  // The list row carries only summary fields; the forwarded URL and injected keys are fetched on demand
  // from `GET /api/Proxies/{proxyId}/executions/{executionId}`.
  const { data, isLoading, isError } = useGetProxyExecution(proxyId, log.id);
  const detail = data ?? log;
  const errorText = detail.errorMessage || outcomeLabel(detail.outcome);
  const showError = detail.status >= 400 && Boolean(errorText);

  if (isLoading) {
    return (
      <div
        id={logDetailsId(log.id)}
        className="flex items-center gap-2 border-t bg-muted/20 px-4 py-6 text-sm text-muted-foreground"
      >
        <Loader2 className="h-4 w-4 animate-spin" />
        {t("PROXY_LOGS_TAB.LOADING_RESPONSE")}
      </div>
    );
  }

  const curl = buildProxyCurl(detail, typeof window === "undefined" ? "" : window.location.origin);

  const upstreamUrl = detail.upstreamUrl || detail.upstreamHost;

  return (
    <div
      id={logDetailsId(log.id)}
      className="grid gap-3 border-t bg-muted/20 px-4 py-3 text-sm lg:grid-cols-2"
    >
      <div className="flex flex-wrap items-start justify-between gap-4 lg:col-span-2">
        <div className="flex flex-wrap gap-6">
          <div>
            <span className="text-xs font-medium uppercase text-muted-foreground">
              {t("PROXY_LOGS_TAB.FORWARDED_TO")}
            </span>
            <div className="group/url flex items-center gap-1">
              <p className="break-all font-mono">{upstreamUrl || "—"}</p>
              {upstreamUrl ? (
                <CopyButton
                  label="URL"
                  value={upstreamUrl}
                  title={t("PROXY_LOGS_TAB.COPY_THE_FORWARDED_UPSTREAM")}
                  iconOnly
                  className="opacity-0 transition-opacity group-hover/url:opacity-100"
                />
              ) : null}
            </div>
          </div>
          <div>
            <span className="text-xs font-medium uppercase text-muted-foreground">
              {t("PROXY_LOGS_TAB.RESULT")}
            </span>
            <p>
              {t("PROXY_LOGS_TAB.IN_MS", {
                status: detail.status,
                statusText: detail.statusText,
                latencyMs: detail.latencyMs,
              })}
            </p>
          </div>
        </div>
        <div className="flex shrink-0 gap-2">
          <CopyButton
            label="cURL"
            value={curl}
            title={t("PROXY_LOGS_TAB.COPY_AN_APPROXIMATE_CURL")}
          />
          <CopyButton
            label="JSON"
            value={JSON.stringify(detail, null, 2)}
            title={t("PROXY_LOGS_TAB.COPY_THE_WHOLE_LOG")}
          />
        </div>
      </div>
      <div>
        <span className="text-xs font-medium uppercase text-muted-foreground">
          {t("PROXY_LOGS_TAB.ADDED_HEADERS_AND_QUERY")}
        </span>
        <p>
          {[...detail.injectedHeaderKeys, ...detail.injectedQueryKeys].join(", ") ||
            t("PROXY_LOGS_TAB.NONE")}
        </p>
      </div>
      {showError ? (
        <div className="lg:col-span-2">
          <span className="text-xs font-medium uppercase text-muted-foreground">
            {t("common:ERROR")}
          </span>
          <p className="mt-1 text-red-700">{errorText}</p>
        </div>
      ) : null}
      <div className="lg:col-span-2">
        <span className="text-xs font-medium uppercase text-muted-foreground">
          {t("PROXY_LOGS_TAB.RESPONSE_BODY")}
        </span>
        {isError ? (
          <p className="mt-1 text-red-700">{t("PROXY_LOGS_TAB.FAILED_TO_LOAD_THE")}</p>
        ) : (
          <p className="mt-1 text-muted-foreground" data-testid="body-not-saved">
            {t("PROXY_LOGS_TAB.NOT_SAVED_BLOCKS_NEVER")}
          </p>
        )}
      </div>
    </div>
  );
};

export const ProxyLogsTab = ({ proxy, active }: { proxy: Proxy; active: boolean }) => {
  const { t } = useT("logic.proxy.components");
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

  // Pin during render rather than in an effect: the guard makes it settle after one extra pass.
  if (!live && !asOfUtc && servedAsOf) setAsOfUtc(servedAsOf);

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
          <h3 className="mt-5 text-xl font-semibold">{t("PROXY_LOGS_TAB.NO_REQUEST_LOGS_YET")}</h3>
          <p className="mt-2 max-w-md text-sm text-muted-foreground">
            {t("PROXY_LOGS_TAB.REQUESTS_SENT_THROUGH_THIS")}
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
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-sm text-muted-foreground">
            {t("PROXY_LOGS_TAB.OF_REQUESTS", { length: data.length, totalAllCount })}
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
            {live ? t("PROXY_LOGS_TAB.PAUSED") : t("PROXY_LOGS_TAB.LIVE")}
          </Button>
        </div>
      </div>
      <div className="overflow-x-auto rounded-sm border bg-card">
        <table className="min-w-[900px] w-full text-sm">
          <thead className="bg-muted/40 text-xs text-muted-foreground">
            <tr>
              <th colSpan={5} className="p-0 text-left font-medium">
                <div className={cn("grid gap-4 px-4 py-2", logTableGridClass)}>
                  <span aria-hidden="true" />
                  <span role="columnheader">{t("PROXY_LOGS_TAB.COLUMN_TIME")}</span>
                  <span role="columnheader">{t("PROXY_LOGS_TAB.COLUMN_METHOD")}</span>
                  <span role="columnheader">{t("PROXY_LOGS_TAB.COLUMN_PATH")}</span>
                  <span role="columnheader">{t("PROXY_LOGS_TAB.COLUMN_STATUS")}</span>
                  <span role="columnheader">{t("PROXY_LOGS_TAB.LATENCY")}</span>
                </div>
              </th>
            </tr>
          </thead>
          <tbody>
            {!data.length ? (
              <tr>
                <td colSpan={5} className="px-4 py-10 text-center text-muted-foreground">
                  {t("PROXY_LOGS_TAB.NO_REQUESTS_MATCH_THIS")}
                </td>
              </tr>
            ) : (
              data.map((log) => {
                const expanded = expandedId === log.id;
                return (
                  <tr key={log.id} className="border-t align-top">
                    <td colSpan={5} className="p-0">
                      <button
                        type="button"
                        aria-expanded={expanded}
                        aria-controls={expanded ? logDetailsId(log.id) : undefined}
                        className={cn(
                          "grid w-full gap-4 px-4 py-3 text-left hover:bg-muted/40",
                          expanded && "bg-muted/30",
                          logTableGridClass,
                        )}
                        onClick={() => setExpandedId((id) => (id === log.id ? null : log.id))}
                      >
                        <ChevronRight
                          aria-hidden="true"
                          data-testid="log-row-chevron"
                          className={cn(
                            "mt-0.5 h-4 w-4 text-muted-foreground transition-transform",
                            expanded && "rotate-90",
                          )}
                        />
                        <span>{new Date(log.timeUtc).toLocaleTimeString()}</span>
                        <ProxyMethodBadge method={log.method} />
                        <span className="truncate font-mono text-xs">{log.path}</span>
                        <Badge variant="outline" className={cn("w-fit", statusClass(log.status))}>
                          {log.status}
                        </Badge>
                        <span>{t("PROXY_LOGS_TAB.MS", { latencyMs: log.latencyMs })}</span>
                      </button>
                      {expanded ? <LogDetails proxyId={proxy.id} log={log} /> : null}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
      {/* The pager hides itself when there is one page and no page size worth choosing. */}
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
      {isFetching ? (
        <p className="text-xs text-muted-foreground">{t("PROXY_LOGS_TAB.REFRESHING_LOGS")}</p>
      ) : null}
    </div>
  );
};
