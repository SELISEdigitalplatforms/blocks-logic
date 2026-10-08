"use client";

import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ArrowDown, Check, Clock, Copy, Loader2, Play, RefreshCw, Workflow, X } from "lucide-react";
import { Sheet, SheetContent, SheetHeader, SheetTitle } from "@/components/ui-kits/sheet/sheet";
import { Button } from "@/components/ui-kits/button/button";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui-kits/select/select";
import { SearchInput } from "@/components/search-input/search-input";
import { CopyToClipboardButton } from "@/components/copy-to-clipboard-button/copy-to-clipboard-button";
import { cn } from "@/lib/utils";
import { useGetWorkflowExecutionLogs } from "../../hooks/use-workflow-api";
import { EXECUTION_LOG_RETENTION_DAYS } from "../../constants";
import type { ExecutedNode } from "../../models/workflow.model";
import {
  ExecutionLogsAvailability,
  type ExecutionLogEntry,
  type ExecutionLogLevel,
  type WorkflowExecution,
} from "../../types/workflow.service.type";
import { getStatusConfig } from "../../utils/workflow-execution-list.util";
import {
  DEFAULT_EXECUTION_LOG_FILTERS,
  EXECUTION_LOG_LEVELS,
  EXECUTION_LOG_LEVEL_LABELS,
  buildNodeFilterOptions,
  countByLevel,
  filterExecutionLogs,
  formatLogTime,
  formatLogsForCopy,
  isDefaultExecutionLogFilters,
  logEntryNodeLabel,
  nodeFilterValue,
  normalizeLevel,
} from "../../utils/execution-logs.util";

const SEARCH_DEBOUNCE_MS = 200;
const PINNED_TO_BOTTOM_PX = 40;
const logSkeletonClass = "bg-slate-200 dark:bg-muted";
const logSkeletonKeys = Array.from({ length: 8 }, (_, row) => `execution-log-skeleton-${row + 1}`);

/** Icons for the main stages only; every other (or unknown) stage renders without one. */
const StageIcon = ({ stage }: { stage: string }) => {
  const props = { className: "h-3.5 w-3.5", "aria-hidden": true } as const;
  if (stage === "node.started") return <Play {...props} />;
  if (stage === "node.completed") return <Check {...props} />;
  if (stage === "node.failed") return <X {...props} />;
  if (stage === "node.waiting") return <Clock {...props} />;
  if (stage.startsWith("execution.")) return <Workflow {...props} />;
  return null;
};

const levelBorderClass: Record<ExecutionLogLevel, string> = {
  Information: "border-l-muted-foreground/30",
  Warning: "border-l-amber-500",
  Error: "border-l-error",
};

const logEntryKey = (entry: ExecutionLogEntry) =>
  [
    entry.timestamp,
    entry.source,
    entry.level,
    entry.stage,
    entry.nodeId ?? "workflow",
    entry.runIndex ?? "none",
    entry.message,
  ].join("|");

export interface ExecutionLogsPanelProps {
  execution: WorkflowExecution;
  nodeExecutions: Pick<ExecutedNode, "nodeId" | "nodeName" | "runIndex">[];
  open: boolean;
  // eslint-disable-next-line no-unused-vars
  onOpenChange: (open: boolean) => void;
}

const PanelMessage = ({
  children,
  action,
}: {
  children: React.ReactNode;
  action?: React.ReactNode;
}) => (
  <div className="flex h-full flex-col items-center justify-center gap-3 px-6 text-center text-sm text-muted-foreground">
    {children}
    {action}
  </div>
);

export const ExecutionLogsPanel = ({
  execution,
  nodeExecutions,
  open,
  onOpenChange,
}: ExecutionLogsPanelProps) => {
  const {
    data: response,
    isLoading,
    isError,
    isFetching,
    refetch,
  } = useGetWorkflowExecutionLogs({ executionId: execution.id }, { enabled: open });
  const data = response?.data;
  const logs = useMemo(() => data?.logs ?? [], [data?.logs]);
  const live = !!data?.mayStillArrive;

  const [levels, setLevels] = useState<ExecutionLogLevel[]>(DEFAULT_EXECUTION_LOG_FILTERS.levels);
  const [node, setNode] = useState(DEFAULT_EXECUTION_LOG_FILTERS.node);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");

  useEffect(() => {
    const timer = setTimeout(() => setSearch(searchInput), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchInput]);

  const filtersAreDefault = isDefaultExecutionLogFilters({ levels, node, search: searchInput });
  const filtered = useMemo(
    () => filterExecutionLogs(logs, { levels, node, search }),
    [logs, levels, node, search],
  );
  const levelCounts = useMemo(() => countByLevel(logs), [logs]);
  const nodeOptions = useMemo(
    () => buildNodeFilterOptions(logs, nodeExecutions),
    [logs, nodeExecutions],
  );

  const clearFilters = () => {
    setLevels(DEFAULT_EXECUTION_LOG_FILTERS.levels);
    setNode(DEFAULT_EXECUTION_LOG_FILTERS.node);
    setSearchInput("");
    setSearch("");
  };

  const toggleLevel = (level: ExecutionLogLevel) => {
    setLevels((current) => {
      if (current.includes(level)) {
        // At least one level must stay on.
        return current.length === 1 ? current : current.filter((l) => l !== level);
      }
      return EXECUTION_LOG_LEVELS.filter((l) => l === level || current.includes(l));
    });
  };

  // Auto-scroll: stay pinned to the bottom while live, but only if the user is already there.
  const listRef = useRef<HTMLDivElement>(null);
  const pinnedRef = useRef(true);
  const [hasNewBelow, setHasNewBelow] = useState(false);
  const previousCount = useRef(0);

  const scrollToBottom = () => {
    const el = listRef.current;
    if (el) el.scrollTop = el.scrollHeight;
    pinnedRef.current = true;
    setHasNewBelow(false);
  };

  useLayoutEffect(() => {
    const grew = filtered.length > previousCount.current;
    previousCount.current = filtered.length;
    if (!grew || !live) return;
    if (pinnedRef.current) {
      const el = listRef.current;
      if (el) el.scrollTop = el.scrollHeight;
    } else {
      setHasNewBelow(true);
    }
  }, [filtered.length, live]);

  const onListScroll = () => {
    const el = listRef.current;
    if (!el) return;
    const pinned = el.scrollHeight - el.scrollTop - el.clientHeight <= PINNED_TO_BOTTOM_PX;
    pinnedRef.current = pinned;
    if (pinned) setHasNewBelow(false);
  };

  const [copied, setCopied] = useState(false);
  const copyLogs = async () => {
    try {
      await navigator.clipboard.writeText(formatLogsForCopy(filtered));
      setCopied(true);
      setTimeout(() => setCopied(false), 1000);
    } catch (err) {
      console.error("Failed to copy logs:", err);
    }
  };

  const status = getStatusConfig(execution.status);
  const retentionDays =
    data?.retentionDays && data.retentionDays > 0
      ? data.retentionDays
      : EXECUTION_LOG_RETENTION_DAYS;
  const retry = (
    <Button variant="outline" size="sm" onClick={() => refetch()}>
      Retry
    </Button>
  );

  const renderBody = () => {
    if (isLoading) {
      return (
        <output className="block space-y-2 p-4" aria-label="Loading execution logs">
          {logSkeletonKeys.map((key) => (
            <Skeleton key={key} className={cn("h-6 w-full", logSkeletonClass)} />
          ))}
        </output>
      );
    }
    if (isError || !data) {
      return <PanelMessage action={retry}>Couldn&apos;t load logs.</PanelMessage>;
    }
    if (data.availability === ExecutionLogsAvailability.NotRecorded) {
      return (
        <PanelMessage>
          Logs aren&apos;t available for this execution. It ran before execution logging was turned
          on.
        </PanelMessage>
      );
    }
    if (data.availability === ExecutionLogsAvailability.Expired) {
      return (
        <PanelMessage>
          <p className="font-medium text-foreground">Logs expire after {retentionDays} days.</p>
          <p>This execution ran on {new Date(execution.startedAt).toLocaleString()}.</p>
        </PanelMessage>
      );
    }
    if (data.availability === ExecutionLogsAvailability.SourceUnavailable) {
      return (
        <PanelMessage action={retry}>
          Logs can&apos;t be loaded right now. Try again later.
        </PanelMessage>
      );
    }
    if (logs.length === 0) {
      return live ? (
        <PanelMessage>
          <Loader2 className="h-6 w-6 animate-spin text-primary" aria-hidden />
          <p>Waiting for logs... They can take a while to appear.</p>
        </PanelMessage>
      ) : (
        <PanelMessage>No logs were recorded for this execution.</PanelMessage>
      );
    }
    if (filtered.length === 0) {
      return (
        <PanelMessage
          action={
            <Button variant="outline" size="sm" onClick={clearFilters}>
              Clear filters
            </Button>
          }
        >
          No logs match these filters.
        </PanelMessage>
      );
    }
    return (
      <div className="relative h-full">
        <div
          ref={listRef}
          onScroll={onListScroll}
          role="log"
          aria-live={live ? "polite" : "off"}
          className="h-full overflow-y-auto"
          data-testid="execution-logs-list"
        >
          {filtered.map((entry) => (
            <LogRow key={logEntryKey(entry)} entry={entry} onNodeClick={setNode} />
          ))}
        </div>
        {hasNewBelow && (
          <Button
            size="sm"
            className="absolute bottom-3 left-1/2 h-7 -translate-x-1/2 gap-1 rounded-full px-3 text-xs shadow"
            onClick={scrollToBottom}
          >
            New logs <ArrowDown className="h-3 w-3" />
          </Button>
        )}
      </div>
    );
  };

  const showFilters =
    !isLoading &&
    !isError &&
    data?.availability === ExecutionLogsAvailability.Available &&
    logs.length > 0;

  return (
    <Sheet open={open} modal={false} onOpenChange={onOpenChange}>
      <SheetContent
        side="right"
        className="top-[60px] flex h-[calc(100vh-60px)] w-full flex-col gap-0 p-0 sm:max-w-xl"
        hideClose
        aria-describedby={undefined}
        onInteractOutside={(e) => e.preventDefault()}
        data-testid="execution-logs-panel"
      >
        <SheetHeader className="flex-row items-center justify-between space-y-0 border-b px-4 py-3">
          <div className="flex min-w-0 items-center gap-3">
            <SheetTitle className="text-base">Execution logs</SheetTitle>
            <div className="flex items-center gap-1.5">
              <div className={cn("h-2 w-2 rounded-full", status.color)} />
              <span className={cn("text-xs font-medium", status.textClass)}>{status.label}</span>
            </div>
            {live && (
              <span className="flex items-center gap-1.5 text-xs font-medium text-success">
                <span className="h-2 w-2 animate-pulse rounded-full bg-success" />
                <span>Live</span>
              </span>
            )}
          </div>
          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              className="h-8 w-8"
              aria-label="Refresh logs"
              onClick={() => refetch()}
            >
              <RefreshCw className={cn("h-4 w-4", isFetching && "animate-spin")} />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="h-8 w-8"
              aria-label="Close execution logs"
              onClick={() => onOpenChange(false)}
            >
              <X className="h-4 w-4" />
            </Button>
          </div>
        </SheetHeader>

        {showFilters && (
          <div className="flex flex-col gap-2 border-b px-4 py-3">
            <div className="flex flex-wrap items-center gap-2">
              {EXECUTION_LOG_LEVELS.map((level) => {
                const active = levels.includes(level);
                return (
                  <button
                    key={level}
                    type="button"
                    aria-pressed={active}
                    onClick={() => toggleLevel(level)}
                    className={cn(
                      "rounded-full border px-2.5 py-0.5 text-xs font-medium transition-colors",
                      active
                        ? "border-primary bg-primary/10 text-foreground"
                        : "text-muted-foreground",
                    )}
                  >
                    {EXECUTION_LOG_LEVEL_LABELS[level]} {levelCounts[level]}
                  </button>
                );
              })}
              {!filtersAreDefault && (
                <button
                  type="button"
                  className="ml-auto text-xs font-medium text-primary hover:underline"
                  onClick={clearFilters}
                >
                  Clear filters
                </button>
              )}
            </div>
            <div className="flex gap-2">
              <div className="w-48 shrink-0">
                <Select value={node} onValueChange={setNode}>
                  <SelectTrigger className="h-9" aria-label="Filter by node">
                    <SelectValue placeholder="All nodes" />
                  </SelectTrigger>
                  <SelectContent>
                    {nodeOptions.map((option) => (
                      <SelectItem key={option.value} value={option.value}>
                        {option.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="min-w-0 flex-1">
                <SearchInput
                  value={searchInput}
                  onSearch={setSearchInput}
                  placeholder="Search logs"
                  className="h-9"
                  isVisible
                  setIsVisible={() => undefined}
                />
              </div>
            </div>
          </div>
        )}

        <div className="min-h-0 flex-1">{renderBody()}</div>

        {data && !isLoading && !isError && (
          <div className="flex flex-wrap items-center justify-between gap-2 border-t px-4 py-2 text-xs text-muted-foreground">
            <div className="flex flex-col gap-0.5">
              {data.availability === ExecutionLogsAvailability.Available && (
                <span>
                  Showing {filtered.length} of {logs.length} lines
                </span>
              )}
              {data.isTruncated && <span>Not all lines are shown.</span>}
              {data.traceId && (
                <CopyToClipboardButton textToCopy={data.traceId}>
                  <span className="font-mono">Trace {data.traceId}</span>
                </CopyToClipboardButton>
              )}
            </div>
            {filtered.length > 0 && (
              <Button variant="outline" size="sm" className="h-8 gap-1.5" onClick={copyLogs}>
                {copied ? <Check className="h-3.5 w-3.5" /> : <Copy className="h-3.5 w-3.5" />}
                {copied ? "Copied" : "Copy logs"}
              </Button>
            )}
          </div>
        )}
      </SheetContent>
    </Sheet>
  );
};

const LogRow = ({
  entry,
  // eslint-disable-next-line no-unused-vars
  onNodeClick,
}: {
  entry: ExecutionLogEntry;
  // eslint-disable-next-line no-unused-vars
  onNodeClick: (value: string) => void;
}) => {
  const level = normalizeLevel(entry.level);
  const nodeFilter = entry.nodeId ? nodeFilterValue(entry.nodeId) : null;
  return (
    <div
      className={cn(
        "flex items-start gap-2 border-b border-l-[3px] px-3 py-1.5 text-xs",
        levelBorderClass[level],
      )}
      data-testid="execution-log-row"
    >
      <time
        className="shrink-0 font-mono text-muted-foreground"
        dateTime={entry.timestamp}
        title={entry.timestamp}
      >
        {formatLogTime(entry.timestamp)}
      </time>
      <span
        className="flex h-4 w-4 shrink-0 items-center justify-center text-muted-foreground"
        title={entry.stage}
      >
        <StageIcon stage={entry.stage} />
      </span>
      {level !== "Information" && (
        <Badge
          variant={level === "Error" ? "error" : "outline"}
          className={cn(
            "shrink-0 px-1.5 py-0 text-[10px]",
            level === "Warning" && "border-amber-500 text-amber-700",
          )}
        >
          {EXECUTION_LOG_LEVEL_LABELS[level]}
        </Badge>
      )}
      {nodeFilter ? (
        <button
          type="button"
          className="max-w-[140px] shrink-0 truncate rounded bg-muted px-1.5 font-medium hover:bg-muted/70"
          title={`Show only ${logEntryNodeLabel(entry)}`}
          onClick={() => onNodeClick(nodeFilter)}
        >
          {logEntryNodeLabel(entry)}
          {entry.runIndex != null && (
            <span className="text-muted-foreground">#{entry.runIndex}</span>
          )}
        </button>
      ) : (
        <span className="shrink-0 rounded bg-muted px-1.5 font-medium">Workflow</span>
      )}
      <span className="min-w-0 flex-1 whitespace-pre-wrap break-words">{entry.message}</span>
    </div>
  );
};
