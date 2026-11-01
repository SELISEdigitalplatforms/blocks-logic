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
import { useT, tStatic } from "@seliseblocks/genesis-os";

const LOGIC_FUNCTIONS_COMPONENTS_MODULE = "logic.functions.components";

/**
 * Design's grid: Run · Status · Triggered by · Duration · Peak mem · Version · row actions.
 * The last track holds the kebab, the row's only control. It was 18 px — the prototype's
 * chevron-only width — which left the menu button overflowing across the Version column.
 */
const GRID =
  "grid grid-cols-[minmax(0,1.1fr)_112px_minmax(0,0.9fr)_84px_92px_78px_28px] items-center gap-3";

const TRIGGER_LABELS: Record<string, string> = {
  Http: "HTTP",
  get Workflow() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "RUNS_TABLE.WORKFLOW");
  },
  get Test() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "RUNS_TABLE.TEST");
  },
  get Replay() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "RUNS_TABLE.REPLAY");
  },
  get Schedule() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "RUNS_TABLE.SCHEDULE");
  },
  get Event() {
    return tStatic(LOGIC_FUNCTIONS_COMPONENTS_MODULE, "RUNS_TABLE.EVENT");
  },
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
  const { t } = useT(LOGIC_FUNCTIONS_COMPONENTS_MODULE);
  let emptyMessage = t("RUNS_TABLE.NO_RUNS_YET_DEPLOY");
  if (hasFilters) emptyMessage = t("RUNS_TABLE.NO_RUNS_MATCH_THIS");
  else if (isDeployed) emptyMessage = t("RUNS_TABLE.NO_RUNS_YET_CALL");
  const { mutateAsync: replayAsync } = useReplayRun();
  const { mutateAsync: cancelAsync } = useCancelRun();

  const handleReplay = async (runId: string) => {
    try {
      await replayAsync(runId);
      showSuccessToast({ description: t("RUNS_TABLE.REPLAY_STARTED") });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("RUNS_TABLE.FAILED_TO_REPLAY_RUN") });
    }
  };

  const handleCancel = async (runId: string) => {
    try {
      await cancelAsync(runId);
      showSuccessToast({ description: t("RUNS_TABLE.CANCEL_REQUESTED") });
    } catch (error) {
      if (isErrorWithErrors(error)) return showErrorToast({ errors: error.errors });
      return showErrorToast({ errors: t("RUNS_TABLE.FAILED_TO_CANCEL_RUN") });
    }
  };

  return (
    <div className="flex flex-col gap-3">
      <div
        className="overflow-hidden rounded-lg border"
        role="grid"
        aria-label={t("RUNS_TABLE.RUNS")}
      >
        <div /* NOSONAR: CSS-grid table */
          className={`${GRID} border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis`}
          role="row"
        >
          <span /* NOSONAR: CSS-grid table */ role="columnheader">{t("RUNS_TABLE.RUN")}</span>
          <span role="columnheader">{t("common:STATUS")}</span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader" className="whitespace-nowrap">
            {t("RUNS_TABLE.TRIGGERED_BY")}
          </span>
          <span /* NOSONAR: CSS-grid table */ role="columnheader">{t("RUNS_TABLE.DURATION")}</span>
          <span role="columnheader" className="whitespace-nowrap">
            {t("RUNS_TABLE.PEAK_MEM")}
          </span>
          <span role="columnheader">{t("RUNS_TABLE.VERSION")}</span>
          <span role="columnheader" aria-label={t("RUNS_TABLE.ROW_ACTIONS")} />
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
          <p className="px-5 py-9 text-center text-sm text-medium-emphasis">{emptyMessage}</p>
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
                  {run.attempt > 1 ? t("RUNS_TABLE.ATTEMPT", { attempt: run.attempt }) : ""}
                </span>

                <span className="whitespace-nowrap text-xs" role="gridcell">
                  {formatDuration(run.durationMs)}
                  {/* Only runs of a function with "Reuse sandbox" on say warm or cold. */}
                  {run.reused != null && (
                    <span
                      className="text-low-emphasis"
                      title={run.reused ? t("RUNS_TABLE.WARM_REUSED") : t("RUNS_TABLE.COLD_START")}
                    >
                      {run.reused ? t("RUNS_TABLE.WARM") : t("RUNS_TABLE.COLD")}
                    </span>
                  )}
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
                        aria-label={t("RUNS_TABLE.ACTIONS_FOR_RUN", { id: run.id })}
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
                        <span>{t("RUNS_TABLE.OPEN")}</span>
                      </DropdownMenuItem>
                      {isActive ? (
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => void handleCancel(run.id)}
                        >
                          <Ban className="mr-2 h-4 w-4" />
                          <span>{t("common:CANCEL")}</span>
                        </DropdownMenuItem>
                      ) : (
                        <DropdownMenuItem
                          className="cursor-pointer"
                          onClick={() => void handleReplay(run.id)}
                        >
                          <RotateCcw className="mr-2 h-4 w-4" />
                          <span>{t("RUNS_TABLE.REPLAY")}</span>
                        </DropdownMenuItem>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>
            );
          })}
      </div>

      <p className="text-xs text-medium-emphasis">{t("RUNS_TABLE.EVERY_TRIGGER_HTTP_WORKFLOW")}</p>
    </div>
  );
};
