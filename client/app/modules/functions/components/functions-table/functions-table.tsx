"use client";
import { KeyboardEvent, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { Copy, EllipsisVertical, ArrowRightFromLine, Trash, Zap } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { Button } from "@/components/ui-kits/button/button";
import { showSuccessToast } from "@/hooks/use-toast";
import { IFunctionSummary } from "../../types/function.types";
import { formatAbsoluteTime, formatRelativeTime, formatRunCount } from "../../utils/format";
import { FunctionStatusChip } from "../function-status-chip";
import { DeleteFunctionDialog } from "../delete-function-dialog";
import { buildInvokePath, buildInvokeUrl } from "../endpoint-badge";

/**
 * Design's grid: Function · Invoked by · Active · Runs 24 h · Last run · row actions.
 *
 * Every track is `fr` or a fixed width on purpose. The header and each row are separate grid
 * containers, so an `auto` track would be measured against *that container's* content — a row
 * showing "—" and one showing "3 minutes ago" would size their columns differently, and neither
 * would line up with the header. The last track fits the kebab, which is the row's only control:
 * the chevron that used to sit beside it said nothing the whole-row click did not already say.
 */
const GRID =
  "grid grid-cols-[minmax(0,1.6fr)_minmax(0,0.85fr)_78px_76px_104px_28px] items-center gap-4";

const HEADER_CLASS =
  "border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis";

const FunctionsEmptyState = ({ onCreateFunction }: { onCreateFunction: () => void }) => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
      <Zap className="h-7 w-7" />
    </div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">No functions yet</h4>
    <p className="mt-2 max-w-md text-sm text-medium-emphasis">
      A function is a small Node handler you deploy here. Every invocation runs in its own sandbox,
      with its own limits. Call it over HTTP with a Blocks token, or from a workflow&apos;s Function
      step.
    </p>
    <Button className="mt-6" size="sm" onClick={onCreateFunction}>
      New function
    </Button>
  </div>
);

/** Header + rows in the real frame, so the card does not change height or shape on load. */
const FunctionsTableSkeleton = ({ rowCount }: { rowCount: number }) => (
  <div className="overflow-hidden rounded-lg border" aria-busy="true" aria-live="polite">
    <div className={`${GRID} ${HEADER_CLASS}`}>
      <span>Function</span>
      <span className="whitespace-nowrap">Invoked by</span>
      <span>Active</span>
      <span className="whitespace-nowrap">Runs 24 h</span>
      <span className="whitespace-nowrap">Last run</span>
      <span />
    </div>
    {Array.from({ length: rowCount }).map((_, index) => (
      <div key={index} className={`${GRID} border-b px-4 py-3.5 last:border-b-0`}>
        <div className="flex flex-col gap-1.5">
          <Skeleton className="h-4 w-40" />
          <Skeleton className="h-3 w-56" />
        </div>
        <Skeleton className="h-3 w-24" />
        <Skeleton className="h-3 w-8" />
        <Skeleton className="h-3 w-8" />
        <Skeleton className="h-3 w-16" />
        <span />
      </div>
    ))}
  </div>
);

type FunctionsTableProps = {
  functions: IFunctionSummary[];
  isLoading: boolean;
  /** Drives the "no matches" wording — an empty page under a filter is not an empty project. */
  hasFilters?: boolean;
  /** Skeleton rows, so the placeholder is as tall as the page that replaces it. */
  rowCount?: number;
  onCreateFunction: () => void;
};

export const FunctionsTable = ({
  functions,
  isLoading,
  hasFilters,
  rowCount = 10,
  onCreateFunction,
}: FunctionsTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [pendingDelete, setPendingDelete] = useState<IFunctionSummary | null>(null);

  const copyEndpoint = (functionId: string) => {
    navigator.clipboard.writeText(buildInvokeUrl(functionId));
    showSuccessToast({ description: "Endpoint copied." });
  };

  const openFunction = (functionId: string) => navigate(scoped(`functions/${functionId}`));

  // The whole row is the target, so it has to answer the keyboard too. The container is a
  // role="grid" rather than a static table precisely because its rows are activatable.
  const handleRowKeyDown = (event: KeyboardEvent<HTMLDivElement>, functionId: string) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    openFunction(functionId);
  };

  if (isLoading) return <FunctionsTableSkeleton rowCount={rowCount} />;

  if (!functions.length) {
    return hasFilters ? (
      <p className="py-14 text-center text-sm text-medium-emphasis">
        No functions match this search.
      </p>
    ) : (
      <FunctionsEmptyState onCreateFunction={onCreateFunction} />
    );
  }

  return (
    <>
      <div className="overflow-hidden rounded-lg border" role="grid" aria-label="Functions">
        <div className={`${GRID} ${HEADER_CLASS}`} role="row">
          <span role="columnheader">Function</span>
          <span role="columnheader" className="whitespace-nowrap">
            Invoked by
          </span>
          <span role="columnheader">Active</span>
          <span role="columnheader" className="whitespace-nowrap">
            Runs 24 h
          </span>
          <span role="columnheader" className="whitespace-nowrap">
            Last run
          </span>
          <span role="columnheader" aria-label="Row actions" />
        </div>

        {functions.map((fn) => (
          <div
            key={fn.id}
            role="row"
            tabIndex={0}
            className={`${GRID} cursor-pointer border-b px-4 py-3.5 last:border-b-0 hover:bg-surface-app focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring`}
            onClick={() => openFunction(fn.id)}
            onKeyDown={(event) => handleRowKeyDown(event, fn.id)}
          >
            <div className="flex min-w-0 flex-col gap-1" role="gridcell">
              <div className="flex flex-wrap items-center gap-2">
                <Link
                  to={scoped(`functions/${fn.id}`)}
                  className="truncate text-sm font-semibold hover:text-primary hover:underline"
                  // The row is already the link target; let the row handle it once.
                  tabIndex={-1}
                  onClick={(e) => e.stopPropagation()}
                >
                  {fn.name}
                </Link>
                <FunctionStatusChip status={fn.status} />
                {fn.isDirty && fn.status === "Live" && (
                  <span className="whitespace-nowrap text-xs text-medium-emphasis">
                    unpublished changes
                  </span>
                )}
              </div>
              <code className="truncate font-mono text-xs text-medium-emphasis">
                {buildInvokePath(fn.id)}
              </code>
            </div>

            <span
              className="truncate whitespace-nowrap text-xs text-medium-emphasis"
              role="gridcell"
            >
              {fn.httpEnabled && fn.workflowEnabled
                ? "HTTP · Workflow"
                : fn.workflowEnabled
                  ? "Workflow"
                  : "HTTP"}
            </span>

            <code className="font-mono text-xs font-semibold text-primary" role="gridcell">
              {fn.activeVersionNumber ? `v${fn.activeVersionNumber}` : "—"}
            </code>

            <span className="whitespace-nowrap text-xs" role="gridcell">
              {formatRunCount(fn.runs24h)}
            </span>

            <span
              className="min-w-0 truncate whitespace-nowrap text-xs text-medium-emphasis"
              role="gridcell"
            >
              {fn.lastRunAt ? (
                <Tooltip>
                  <TooltipTrigger asChild>
                    <span>{formatRelativeTime(fn.lastRunAt)}</span>
                  </TooltipTrigger>
                  <TooltipContent>{formatAbsoluteTime(fn.lastRunAt)}</TooltipContent>
                </Tooltip>
              ) : (
                "—"
              )}
            </span>

            <div
              className="flex items-center justify-end gap-0.5"
              role="gridcell"
              onClick={(e) => e.stopPropagation()}
            >
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button
                    variant="ghost"
                    aria-label={`Actions for ${fn.name}`}
                    className="h-6 w-6 shrink-0 p-0"
                  >
                    <EllipsisVertical className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem className="cursor-pointer" onClick={() => openFunction(fn.id)}>
                    <ArrowRightFromLine className="mr-2 h-4 w-4" />
                    <span>Open</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem className="cursor-pointer" onClick={() => copyEndpoint(fn.id)}>
                    <Copy className="mr-2 h-4 w-4" />
                    <span>Copy endpoint</span>
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    className="cursor-pointer text-error"
                    onClick={() => setPendingDelete(fn)}
                  >
                    <Trash className="mr-2 h-4 w-4" />
                    <span>Delete</span>
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          </div>
        ))}
      </div>

      <DeleteFunctionDialog
        open={!!pendingDelete}
        onOpenChange={(value) => {
          if (!value) setPendingDelete(null);
        }}
        fn={pendingDelete}
      />
    </>
  );
};
