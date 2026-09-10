"use client";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { ChevronRight, Copy, EllipsisVertical, ArrowRightFromLine, Trash, Zap } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui-kits/tooltip/tooltip";
import { Button } from "@/components/ui-kits/button/button";
import { showSuccessToast } from "@/hooks/use-toast";
import { IFunctionSummary } from "../../types/function.types";
import { FUNCTION_INVOKE_ENDPOINT_BASE } from "../../constants/endpoint.constant";
import { formatAbsoluteTime, formatRelativeTime, formatRunCount } from "../../utils/format";
import { FunctionStatusChip } from "../function-status-chip";
import { DeleteFunctionDialog } from "../delete-function-dialog";
import { buildInvokeUrl } from "../endpoint-badge";

/** Design's grid: Function · Invoked by · Active · Runs 24 h · Last run · chevron. */
const GRID =
  "grid grid-cols-[minmax(0,1.6fr)_minmax(0,0.85fr)_78px_minmax(0,auto)_minmax(0,auto)_18px] items-center gap-4";

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

type FunctionsTableProps = {
  functions: IFunctionSummary[];
  isLoading: boolean;
  /** Drives the "no matches" wording — an empty page under a filter is not an empty project. */
  hasFilters?: boolean;
  onCreateFunction: () => void;
};

export const FunctionsTable = ({
  functions,
  isLoading,
  hasFilters,
  onCreateFunction,
}: FunctionsTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [pendingDelete, setPendingDelete] = useState<IFunctionSummary | null>(null);

  const copyEndpoint = (functionId: string) => {
    navigator.clipboard.writeText(buildInvokeUrl(functionId));
    showSuccessToast({ description: "Endpoint copied." });
  };

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2 py-2">
        {Array.from({ length: 6 }).map((_, index) => (
          <Skeleton key={index} className="h-14 w-full" />
        ))}
      </div>
    );
  }

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
      <div className="overflow-hidden rounded-lg border">
        <div
          className={`${GRID} border-b bg-surface-app px-4 py-2.5 text-xs font-medium uppercase tracking-wide text-low-emphasis`}
        >
          <span>Function</span>
          <span>Invoked by</span>
          <span>Active</span>
          <span className="whitespace-nowrap">Runs 24 h</span>
          <span>Last run</span>
          <span />
        </div>

        {functions.map((fn) => (
          <div
            key={fn.id}
            className={`${GRID} cursor-pointer border-b px-4 py-3.5 last:border-b-0 hover:bg-surface-app`}
            onClick={() => navigate(scoped(`functions/${fn.id}`))}
          >
            <div className="flex min-w-0 flex-col gap-1">
              <div className="flex flex-wrap items-center gap-2">
                <Link
                  to={scoped(`functions/${fn.id}`)}
                  className="truncate text-sm font-semibold hover:text-primary hover:underline"
                  onClick={(e) => e.stopPropagation()}
                >
                  {fn.name}
                </Link>
                <FunctionStatusChip status={fn.status} />
                {fn.isDirty && fn.status === "Live" && (
                  <span className="text-xs text-medium-emphasis">unpublished changes</span>
                )}
              </div>
              <code className="truncate font-mono text-xs text-medium-emphasis">
                {FUNCTION_INVOKE_ENDPOINT_BASE}/{fn.id}
              </code>
            </div>

            <span className="text-xs text-medium-emphasis">
              {fn.httpEnabled && fn.workflowEnabled
                ? "HTTP · Workflow"
                : fn.workflowEnabled
                  ? "Workflow"
                  : "HTTP"}
            </span>

            <code className="font-mono text-xs font-semibold text-primary">
              {fn.activeVersionNumber ? `v${fn.activeVersionNumber}` : "—"}
            </code>

            <span className="whitespace-nowrap text-xs">{formatRunCount(fn.runs24h)}</span>

            {fn.lastRunAt ? (
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="whitespace-nowrap text-xs text-medium-emphasis">
                    {formatRelativeTime(fn.lastRunAt)}
                  </span>
                </TooltipTrigger>
                <TooltipContent>{formatAbsoluteTime(fn.lastRunAt)}</TooltipContent>
              </Tooltip>
            ) : (
              <span className="whitespace-nowrap text-xs text-medium-emphasis">—</span>
            )}

            <div className="flex items-center justify-end" onClick={(e) => e.stopPropagation()}>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" aria-label={`Actions for ${fn.name}`} className="h-6 w-6 p-0">
                    <EllipsisVertical className="h-4 w-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem
                    className="cursor-pointer"
                    onClick={() => navigate(scoped(`functions/${fn.id}`))}
                  >
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
              <ChevronRight className="h-4 w-4 shrink-0 text-low-emphasis" />
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
