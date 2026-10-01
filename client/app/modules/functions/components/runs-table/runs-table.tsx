"use client";
import { EllipsisVertical, ArrowRightFromLine, RotateCcw, Ban } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { Button } from "@/components/ui-kits/button/button";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { IRunSummary, TERMINAL_RUN_STATUSES } from "../../types/run.types";
import {
  formatAbsoluteTime,
  formatDuration,
  formatMemoryAgainstLimit,
  formatRelativeTime,
} from "../../utils/format";
import { RunStatusChip } from "../run-status-chip";
import { useCancelRun, useReplayRun } from "../../hooks/use-runs";

/**
 * Design's grid: Run · Status · Triggered by · Duration · Peak mem · Version · row actions.
 * The last track holds the kebab, the row's only control. It was 18 px — the prototype's
 * chevron-only width — which left the menu button overflowing across the Version column.
 */
const GRID =
  "grid grid-cols-[minmax(0,1.1fr)_112px_minmax(0,0.9fr)_84px_92px_78px_28px] items-center gap-3";

const TRIGGER_LABELS: Record<string, string> = {
  Http: "HTTP",
  Workflow: "Workflow",
  Test: "Test",
  Replay: "Replay",
  Schedule: "Schedule",
  Event: "Event",
};

type RunsTableProps = {
  runs: IRunSummary[];
  isLoading: boolean;
  /** The memory limit these runs were given, so peak memory reads as "82 / 192 MB". */
  memoryLimitMb?: number | null;
  /** Empty-state wording: a filtered page with no rows is not a function with no runs. */
  hasFilters?: boolean;
  isDeployed?: boolean;
  onOpenRun: (runId: string) => void;
};

export const RunsTable = ({
  runs,
  isLoading,
  memoryLimitMb,
  hasFilters,
  isDeployed,
  onOpenRun,
}: RunsTableProps) => {
  const { mutateAsync: replayAsync } = useReplayRun();
  const { mutateAsync: cancelAsync } = useCancelRun();

  const handleReplay = async (runId: string) => {
    try {
      await replayAsync(runId);
      showSuccessToast({ description: "Replay started." });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to replay run" });
    }
  };

  const handleCancel = async (runId: string) => {
    try {
      await cancelAsync(runId);
      showSuccessToast({ description: "Cancel requested." });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: "Failed to cancel run" });
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div className="overflow-hidden rounded-lg border" role="grid" aria-label="Runs">
        <div
          className={`${GRID} border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis`}
          role="row"
        >
          <span role="columnheader">Run</span>
          <span role="columnheader">Status</span>
          <span role="columnheader" className="whitespace-nowrap">
            Triggered by
          </span>
          <span role="columnheader">Duration</span>
          <span role="columnheader" className="whitespace-nowrap">
            Peak mem
          </span>
          <span role="columnheader">Version</span>
          <span role="columnheader" aria-label="Row actions" />
        </div>

        {isLoading &&
          Array.from({ length: 6 }).map((_, index) => (
            // Same frame as a loaded row, so the card keeps its shape and height on load.
            <div key={index} className={`${GRID} border-b px-4 py-3 last:border-b-0`}>
              <div className="flex flex-col gap-1">
                <Skeleton className="h-3.5 w-32" />
                <Skeleton className="h-3 w-20" />
              </div>
              <Skeleton className="h-5 w-20" />
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-3 w-12" />
              <Skeleton className="h-3 w-16" />
              <Skeleton className="h-3 w-8" />
              <span />
            </div>
          ))}

        {!isLoading && runs.length === 0 && (
          <p className="px-5 py-9 text-center text-sm text-medium-emphasis">
            {hasFilters
              ? "No runs match this filter."
              : isDeployed
                ? "No runs yet — call the endpoint or run a test from the Code tab."
                : "No runs yet — deploy the function and call its endpoint."}
          </p>
        )}

        {!isLoading &&
          runs.map((run) => {
            const isActive = !TERMINAL_RUN_STATUSES.includes(run.status);
            return (
              <div
                key={run.id}
                role="row"
                tabIndex={0}
                className={`${GRID} cursor-pointer border-b px-4 py-3 last:border-b-0 hover:bg-surface-app focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring`}
                onClick={() => onOpenRun(run.id)}
                onKeyDown={(event) => {
                  if (event.key !== "Enter" && event.key !== " ") return;
                  event.preventDefault();
                  onOpenRun(run.id);
                }}
              >
                <div className="flex min-w-0 flex-col gap-0.5" role="gridcell">
                  <code className="truncate font-mono text-xs font-semibold">{run.id}</code>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <span className="text-xs text-low-emphasis">
                        {formatRelativeTime(run.createdDate)}
                      </span>
                    </TooltipTrigger>
                    <TooltipContent>{formatAbsoluteTime(run.createdDate)}</TooltipContent>
                  </Tooltip>
                </div>

                <span role="gridcell">
                  <RunStatusChip status={run.status} />
                </span>

                <span className="min-w-0 truncate text-xs text-medium-emphasis" role="gridcell">
                  {TRIGGER_LABELS[run.invokedBy] ?? run.invokedBy}
                  {run.attempt > 1 ? ` · attempt ${run.attempt}` : ""}
                </span>

                <span className="whitespace-nowrap text-xs" role="gridcell">
                  {formatDuration(run.durationMs)}
                </span>

                <span className="whitespace-nowrap text-xs" role="gridcell">
                  {formatMemoryAgainstLimit(run.peakMemoryBytes, memoryLimitMb)}
                </span>

                <code className="font-mono text-xs text-primary" role="gridcell">
                  v{run.versionNumber}
                </code>

                <div
                  className="flex items-center justify-end gap-0.5"
                  role="gridcell"
                  onClick={(e) => e.stopPropagation()}
                >
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        aria-label={`Actions for run ${run.id}`}
                        className="h-6 w-6 shrink-0 p-0"
                      >
                        <EllipsisVertical className="h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => onOpenRun(run.id)}
                      >
                        <ArrowRightFromLine className="mr-2 h-4 w-4" />
                        <span>Open</span>
                      </DropdownMenuItem>
                      {isActive ? (
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => void handleCancel(run.id)}
                        >
                          <Ban className="mr-2 h-4 w-4" />
                          <span>Cancel</span>
                        </DropdownMenuItem>
                      ) : (
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => void handleReplay(run.id)}
                        >
                          <RotateCcw className="mr-2 h-4 w-4" />
                          <span>Replay</span>
                        </DropdownMenuItem>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
            );
          })}
      </div>

      <p className="text-xs text-medium-emphasis">
        Runs and their logs are kept 30 days. Every trigger — HTTP, workflow or a test from the
        editor — lands here.
      </p>
    </div>
  );
};
