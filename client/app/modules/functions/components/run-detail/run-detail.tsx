import { useMemo, useState } from "react";
import { Ban, Copy, Download, Loader2, RotateCcw, Search } from "lucide-react";
import { Card, CardContent } from "@/components/ui-kits/card/card";
import { Button } from "@/components/ui-kits/button/button";
import { Input } from "@/components/ui-kits/input/input";
import { cn } from "@/lib/utils";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { useCancelRun, useGetRun, useGetRunLogs, useReplayRun } from "../../hooks/use-runs";
import { explainRunError } from "../../constants/run-error.constant";
import { IRunDetail, TERMINAL_RUN_STATUSES } from "../../types/run.types";
import {
  formatAbsoluteTime,
  formatDuration,
  formatMemoryAgainstLimit,
  formatRelativeTime,
  formatTimeOfDay,
} from "../../utils/format";
import { RunStatusChip } from "../run-status-chip";

const LOG_LEVEL_FILTERS = ["All", "Info", "Warn", "Error"] as const;

const LOG_LEVEL_CLASS: Record<string, string> = {
  error: "text-error",
  warn: "text-warning-800",
  info: "text-primary",
  debug: "text-low-emphasis",
};

const TRIGGER_LABELS: Record<string, string> = {
  Http: "HTTP",
  Workflow: "Workflow",
  Test: "Test",
  Replay: "Replay",
  Schedule: "Schedule",
  Event: "Event",
};

/**
 * The stages the run actually recorded. `OUTPUTS` only appears when the function had output
 * actions to process, and the terminal stage is whatever status the run ended in — the design's
 * timeline, not a fixed five-step stepper.
 */
const buildStages = (run: IRunDetail) => {
  const stages: { label: string; at?: string | null; isTerminal?: boolean }[] = [
    { label: "QUEUED", at: run.createdDate },
  ];
  if (run.startedAt) stages.push({ label: "RUNNING", at: run.startedAt });
  if (run.outputResults.length > 0) stages.push({ label: "OUTPUTS", at: run.completedAt });
  if (TERMINAL_RUN_STATUSES.includes(run.status)) {
    stages.push({
      label: run.status.replace(/([a-z])([A-Z])/g, "$1_$2").toUpperCase(),
      at: run.completedAt,
      isTerminal: true,
    });
  } else {
    stages.push({ label: run.status.toUpperCase(), at: null });
  }
  return stages;
};

const JsonPane = ({
  title,
  value,
  emptyText,
  onUseAsTestInput,
}: {
  title: string;
  value?: string | null;
  emptyText: string;
  onUseAsTestInput?: (value: string) => void;
}) => (
  <Card className="min-w-0 flex-1 basis-[320px] overflow-hidden">
    <div className="flex items-center justify-between gap-3 border-b px-4 py-2.5">
      <span className="text-xs font-semibold">{title}</span>
      {value && (
        <div className="flex items-center gap-1">
          {onUseAsTestInput && (
            <Button
              variant="ghost"
              size="xs"
              className="px-2 text-xs"
              onClick={() => onUseAsTestInput(value)}
            >
              Use as test input
            </Button>
          )}
          <Button
            variant="ghost"
            size="icon"
            aria-label={`Copy ${title.toLowerCase()}`}
            className="h-7 w-7 text-medium-emphasis"
            onClick={() => navigator.clipboard.writeText(value)}
          >
            <Copy className="h-3.5 w-3.5" />
          </Button>
        </div>
      )}
    </div>
    {value ? (
      <pre className="max-h-64 overflow-auto bg-surface-app px-4 py-3 font-mono text-xs leading-relaxed">
        {value}
      </pre>
    ) : (
      <p className="px-4 py-6 text-center text-xs text-medium-emphasis">{emptyText}</p>
    )}
  </Card>
);

type RunDetailProps = {
  runId: string;
  /** The limit the run was given, so peak memory reads against it. */
  memoryLimitMb?: number | null;
  /** Loads a run's input into the Code tab's test panel. */
  onUseAsTestInput?: (input: string) => void;
};

export const RunDetail = ({ runId, memoryLimitMb, onUseAsTestInput }: RunDetailProps) => {
  const { data: run, isLoading } = useGetRun({ runId });
  const { data: logsData, isLoading: isLogsLoading } = useGetRunLogs(runId, 0, 500);
  const { mutateAsync: replayAsync, isPending: isReplaying } = useReplayRun();
  const { mutateAsync: cancelAsync, isPending: isCancelling } = useCancelRun();
  const [logLevel, setLogLevel] = useState<(typeof LOG_LEVEL_FILTERS)[number]>("All");
  const [logSearch, setLogSearch] = useState("");

  // Memoised together: `?? []` is a fresh array each render, which would defeat the filter's memo.
  const logs = useMemo(() => logsData?.data ?? [], [logsData?.data]);
  const visibleLogs = useMemo(
    () =>
      logs.filter((line) => {
        const matchesLevel =
          logLevel === "All" || line.level.toLowerCase() === logLevel.toLowerCase();
        const matchesSearch =
          !logSearch || line.message.toLowerCase().includes(logSearch.toLowerCase());
        return matchesLevel && matchesSearch;
      }),
    [logs, logLevel, logSearch],
  );

  if (isLoading || !run) {
    return <Loader2 className="h-6 w-6 animate-spin text-primary" />;
  }

  const isActive = !TERMINAL_RUN_STATUSES.includes(run.status);
  const explanation = explainRunError(run.errorCode, run.errorMessage);
  const stages = buildStages(run);

  const metrics = [
    { label: "Duration", value: formatDuration(run.durationMs) },
    {
      label: "Peak memory",
      value: formatMemoryAgainstLimit(run.peakMemoryBytes, memoryLimitMb),
    },
    {
      label: "Attempts",
      value: `${run.attempt} / ${run.maxAttempts}`,
      isWarning: run.attempt > 1,
    },
    { label: "Version", value: `v${run.versionNumber}`, isPrimary: true },
    { label: "Triggered by", value: TRIGGER_LABELS[run.invokedBy] ?? run.invokedBy },
  ];

  const handleReplay = async () => {
    try {
      await replayAsync(run.id);
      showSuccessToast({
        description: "Replayed as a new run with the same input and idempotency key.",
      });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to replay run" });
    }
  };

  const handleCancel = async () => {
    try {
      await cancelAsync(run.id);
      showSuccessToast({ description: "Cancel requested." });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to cancel run" });
    }
  };

  const downloadLogs = () => {
    const ndjson = logs.map((line) => JSON.stringify(line)).join("\n");
    const url = URL.createObjectURL(new Blob([ndjson], { type: "application/x-ndjson" }));
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = `${run.id}.ndjson`;
    anchor.click();
    URL.revokeObjectURL(url);
  };

  return (
    <div className="flex flex-col gap-3.5">
      <div className="flex flex-wrap items-center justify-end gap-2">
        {isActive ? (
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            disabled={isCancelling}
            onClick={() => void handleCancel()}
          >
            <Ban className="h-3.5 w-3.5" />
            Cancel
          </Button>
        ) : (
          <Button
            variant="outline"
            size="sm"
            className="gap-1.5"
            disabled={isReplaying}
            onClick={() => void handleReplay()}
          >
            <RotateCcw className="h-3.5 w-3.5" />
            Replay
          </Button>
        )}
      </div>

      <Card>
        <CardContent className="flex flex-col gap-4 p-5">
          <div className="flex flex-wrap items-center gap-3">
            <code className="font-mono text-sm font-semibold">{run.id}</code>
            <RunStatusChip status={run.status} />
            <span className="text-xs text-medium-emphasis">
              {formatRelativeTime(run.createdDate)} ({formatAbsoluteTime(run.createdDate)}) ·
              triggered by {TRIGGER_LABELS[run.invokedBy] ?? run.invokedBy}
            </span>
          </div>

          <div className="flex flex-wrap items-start">
            {stages.map((stage, index) => (
              <div key={stage.label} className="flex items-start">
                <div className="flex flex-col items-center gap-1 px-1.5">
                  <span
                    className={cn(
                      "h-2.5 w-2.5 rounded-full",
                      stage.isTerminal ? "bg-success" : "bg-primary",
                      !stage.at && "bg-low-emphasis",
                    )}
                  />
                  <span className="text-[10px] font-semibold tracking-wide">{stage.label}</span>
                  <span className="text-[10px] text-low-emphasis">
                    {stage.at ? formatTimeOfDay(stage.at) : "—"}
                  </span>
                </div>
                {index < stages.length - 1 && (
                  <span className="mt-1 h-0.5 w-7 bg-border" aria-hidden="true" />
                )}
              </div>
            ))}
          </div>

          <div className="flex flex-wrap gap-x-8 gap-y-3 border-t pt-3.5">
            {metrics.map((metric) => (
              <div key={metric.label} className="flex flex-col gap-0.5">
                <span className="text-[10px] font-medium uppercase tracking-wide text-low-emphasis">
                  {metric.label}
                </span>
                <span
                  className={cn(
                    "text-sm font-semibold",
                    metric.isPrimary && "text-primary",
                    metric.isWarning && "text-warning-800",
                  )}
                >
                  {metric.value}
                </span>
              </div>
            ))}
          </div>
        </CardContent>
      </Card>

      {explanation && (
        <Card className="border-error/30 bg-error/5">
          <CardContent className="flex flex-col gap-1 p-4">
            {run.errorCode && (
              <code className="font-mono text-xs font-semibold text-error">{run.errorCode}</code>
            )}
            <p className="text-xs leading-relaxed text-error">{explanation}</p>
          </CardContent>
        </Card>
      )}

      {run.attempts.length > 1 && (
        <Card className="overflow-hidden">
          <div className="border-b px-4 py-3 text-sm font-semibold">Attempts</div>
          {run.attempts.map((attempt) => (
            <div
              key={attempt.number}
              className="flex flex-wrap items-center gap-3 border-b px-4 py-2.5 last:border-b-0"
            >
              <span className="w-[74px] shrink-0 text-xs font-semibold">
                Attempt {attempt.number}
              </span>
              <RunStatusChip status={attempt.status} />
              <span className="min-w-0 flex-1 break-words text-xs text-medium-emphasis">
                {attempt.errorMessage ??
                  (attempt.durationMs != null
                    ? `finished in ${formatDuration(attempt.durationMs)}`
                    : "—")}
              </span>
            </div>
          ))}
        </Card>
      )}

      <Card className="overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b px-4 py-2.5">
          <span className="text-sm font-semibold">Logs</span>
          <div className="flex flex-wrap items-center gap-2">
            <div className="relative">
              <Search className="absolute left-2 top-1/2 h-3 w-3 -translate-y-1/2 text-medium-emphasis" />
              <Input
                aria-label="Search logs"
                placeholder="Search"
                className="h-8 w-36 pl-7 text-xs"
                value={logSearch}
                onChange={(e) => setLogSearch(e.target.value)}
              />
            </div>
            <div className="flex gap-0.5">
              {LOG_LEVEL_FILTERS.map((level) => (
                <button
                  key={level}
                  type="button"
                  aria-pressed={logLevel === level}
                  className={cn(
                    "rounded-md px-2.5 py-1 text-xs transition-colors",
                    logLevel === level
                      ? "bg-blocks-primary-50 font-semibold text-primary"
                      : "font-medium text-medium-emphasis hover:bg-surface-app",
                  )}
                  onClick={() => setLogLevel(level)}
                >
                  {level}
                </button>
              ))}
            </div>
            <Button
              variant="ghost"
              size="xs"
              className="gap-1.5 px-2 text-xs"
              disabled={logs.length === 0}
              onClick={downloadLogs}
            >
              <Download className="h-3.5 w-3.5" />
              Download
            </Button>
          </div>
        </div>

        <div className="max-h-[330px] overflow-auto bg-surface-app px-4 py-3">
          {isLogsLoading && <Loader2 className="h-4 w-4 animate-spin text-medium-emphasis" />}
          {!isLogsLoading && visibleLogs.length === 0 && (
            <p className="text-xs text-medium-emphasis">
              {logs.length === 0 ? "No logs recorded." : "No log lines match this filter."}
            </p>
          )}
          {visibleLogs.map((line) => (
            <div key={line.seq} className="flex gap-3 font-mono text-xs leading-relaxed">
              <span className="shrink-0 text-low-emphasis">{formatTimeOfDay(line.timestamp)}</span>
              <span
                className={cn(
                  "w-11 shrink-0 font-semibold uppercase",
                  LOG_LEVEL_CLASS[line.level.toLowerCase()] ?? "text-medium-emphasis",
                )}
              >
                {line.level}
              </span>
              <span className="min-w-0 whitespace-pre-wrap break-words">{line.message}</span>
            </div>
          ))}
        </div>

        <p className="border-t px-4 py-2.5 text-xs text-low-emphasis">
          Captured from <code className="font-mono">ctx.log</code> and{" "}
          <code className="font-mono">console</code> — 1 MB per run
          {run.logsTruncated ? ", and this run hit that cap" : ", nothing dropped"}.
        </p>
      </Card>

      <div className="flex flex-wrap gap-3.5">
        <JsonPane
          title="Input"
          value={run.input}
          emptyText="No input was sent."
          onUseAsTestInput={onUseAsTestInput}
        />
        <JsonPane
          title="Returned value"
          value={run.result}
          emptyText={
            run.status === "Succeeded"
              ? "The handler returned nothing."
              : "— no value returned; the run did not finish successfully"
          }
        />
      </div>

      {run.outputResults.length > 0 && (
        <Card className="overflow-hidden">
          <div className="border-b px-4 py-3 text-sm font-semibold">Output actions</div>
          {run.outputResults.map((outputResult, index) => (
            <div
              key={outputResult.actionId}
              // Fixed status and timing tracks: as `auto` they were measured per row, so "Sent"
              // and "Skipped" (and "142 ms" vs "200 in 1.2 s · 3 attempts") put every row's
              // right-hand columns at a different offset.
              className="grid grid-cols-[24px_minmax(0,1fr)_72px_168px] items-center gap-3 border-b px-4 py-3 last:border-b-0"
            >
              <span className="flex h-6 w-6 items-center justify-center rounded-full bg-blocks-primary-50 text-xs font-semibold text-primary">
                {index + 1}
              </span>
              <div className="flex min-w-0 flex-col gap-0.5">
                <span className="text-xs font-semibold">
                  {outputResult.kind === "ExternalHttp" ? "External HTTP call" : outputResult.kind}
                </span>
                <code className="truncate font-mono text-xs text-medium-emphasis">
                  {outputResult.actionId}
                </code>
              </div>
              <span
                className={cn(
                  "w-fit rounded px-2 py-0.5 text-[10px] font-semibold uppercase",
                  outputResult.ok
                    ? "bg-success/15 text-success"
                    : outputResult.statusCode == null
                      ? "bg-surface-app text-medium-emphasis"
                      : "bg-error/10 text-error",
                )}
              >
                {outputResult.ok ? "Sent" : outputResult.statusCode == null ? "Skipped" : "Failed"}
              </span>
              <span className="truncate whitespace-nowrap text-xs text-medium-emphasis">
                {outputResult.statusCode ? `${outputResult.statusCode} in ` : ""}
                {formatDuration(outputResult.durationMs)}
                {outputResult.attempts > 1 ? ` · ${outputResult.attempts} attempts` : ""}
              </span>
            </div>
          ))}
        </Card>
      )}
    </div>
  );
};
