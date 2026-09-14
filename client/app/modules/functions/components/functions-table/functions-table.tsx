"use client";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { ArrowRightFromLine, Copy, EllipsisVertical, Plus, Trash } from "lucide-react";
import { FunctionIcon } from "@/constants/navigation-menus";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { Button } from "@/components/ui-kits/button/button";
import { cn } from "@/lib/utils";
import { showSuccessToast } from "@/hooks/use-toast";
import { IFunctionSummary } from "../../types/function.types";
import { formatAbsoluteTime, formatRelativeTime, formatRunCount } from "../../utils/format";
import { FunctionStatusChip } from "../function-status-chip";
import { DeleteFunctionDialog } from "../delete-function-dialog";
import { buildInvokePath, buildInvokeUrl } from "../endpoint-badge";

/**
 * Design's grid: Function · Invoked by · Active · Runs 24 h · Last run · row actions — rendered as
 * the same detached-row table Proxy and Workflow use, so the three service lists read as one
 * product rather than three. The spacing carries the separation (`border-spacing-y-4` with a
 * rounded border per row); the shared `Table` primitives handle the overflow at narrow widths.
 */
const HEAD_CLASS = "px-6 pb-0 pt-2 text-base";

const CELL_CLASS =
  "border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50";

const SKELETON_ROWS = 10;

const FunctionsEmptyState = ({ onCreateFunction }: { onCreateFunction: () => void }) => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
      <FunctionIcon className="h-7 w-7" />
    </div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">No functions yet</h4>
    <p className="mt-2 max-w-md text-sm text-muted-foreground">
      A function is a small Node handler you deploy here. Every invocation runs in its own sandbox,
      with its own limits. Call it over HTTP with a Blocks token, or from a workflow&apos;s Function
      step.
    </p>
    <Button className="mt-6 gap-2" size="sm" onClick={onCreateFunction}>
      <Plus className="h-4 w-4" />
      New function
    </Button>
  </div>
);

/** Rows in the real frame, so the card does not change height or shape on load. */
const FunctionsTableSkeleton = ({ rowCount }: { rowCount: number }) => (
  <Table className="border-separate border-spacing-y-4" aria-busy="true" aria-live="polite">
    <TableBody className="[&_tr:last-child]:border-0">
      {Array.from({ length: rowCount }).map((_, index) => (
        <TableRow key={index} className="border-0">
          <TableCell colSpan={6} className="rounded-lg border border-border bg-background p-4">
            <Skeleton className="h-12 w-full" />
          </TableCell>
        </TableRow>
      ))}
    </TableBody>
  </Table>
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
  rowCount = SKELETON_ROWS,
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
      <Table className="min-w-[920px] border-separate border-spacing-y-4">
        <TableHeader className="[&_tr]:border-0">
          <TableRow className="border-0">
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Function</div>
            </TableHead>
            <TableHead className={HEAD_CLASS}>
              <div className="font-bold text-medium-emphasis">Invoked by</div>
            </TableHead>
            <TableHead className={cn("w-[120px]", HEAD_CLASS)}>
              <div className="font-bold text-medium-emphasis">Active</div>
            </TableHead>
            <TableHead className={cn("w-[140px]", HEAD_CLASS)}>
              <div className="font-bold text-medium-emphasis">Runs 24 h</div>
            </TableHead>
            <TableHead className={cn("w-[180px]", HEAD_CLASS)}>
              <div className="font-bold text-medium-emphasis">Last run</div>
            </TableHead>
            <TableHead className={cn("w-[80px]", HEAD_CLASS)}>
              <span className="sr-only">Row actions</span>
            </TableHead>
          </TableRow>
        </TableHeader>
        <TableBody className="[&_tr:last-child]:border-0">
          {functions.map((fn) => (
            <TableRow
              key={fn.id}
              className="group cursor-pointer border-0 transition-colors"
              onClick={() => openFunction(fn.id)}
            >
              <TableCell className={cn("rounded-l-lg border-l", CELL_CLASS)}>
                <div className="flex min-w-0 flex-col gap-1">
                  <div className="flex flex-wrap items-center gap-2">
                    {/* The row handles the click; the link is what keyboard and screen readers
                        reach the function by, so it keeps its own focus and activation. */}
                    <Link
                      to={scoped(`functions/${fn.id}`)}
                      className="truncate font-semibold hover:text-primary hover:underline"
                      onClick={(event) => event.stopPropagation()}
                    >
                      {fn.name}
                    </Link>
                    <FunctionStatusChip status={fn.status} />
                    {fn.isDirty && fn.status === "Live" && (
                      <span className="whitespace-nowrap text-xs text-muted-foreground">
                        unpublished changes
                      </span>
                    )}
                  </div>
                  <code className="w-[260px] truncate font-mono text-sm text-muted-foreground">
                    {buildInvokePath(fn.id)}
                  </code>
                </div>
              </TableCell>

              <TableCell className={CELL_CLASS}>
                <span className="whitespace-nowrap text-sm text-muted-foreground">
                  {fn.httpEnabled && fn.workflowEnabled
                    ? "HTTP · Workflow"
                    : fn.workflowEnabled
                      ? "Workflow"
                      : "HTTP"}
                </span>
              </TableCell>

              <TableCell className={cn("w-[120px]", CELL_CLASS)}>
                <code className="font-mono text-sm font-semibold text-primary">
                  {fn.activeVersionNumber ? `v${fn.activeVersionNumber}` : "—"}
                </code>
              </TableCell>

              <TableCell className={cn("w-[140px]", CELL_CLASS)}>
                <span className="whitespace-nowrap text-sm">{formatRunCount(fn.runs24h)}</span>
              </TableCell>

              <TableCell className={cn("w-[180px]", CELL_CLASS)}>
                <span className="whitespace-nowrap text-sm text-muted-foreground">
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
              </TableCell>

              <TableCell className={cn("w-[80px] rounded-r-lg border-r", CELL_CLASS)}>
                <div
                  className="flex items-center justify-end"
                  onClick={(event) => event.stopPropagation()}
                >
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        variant="ghost"
                        className="h-5 w-5 p-0"
                        aria-label={`Actions for ${fn.name}`}
                      >
                        <EllipsisVertical width={20} height={20} />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => openFunction(fn.id)}
                      >
                        <ArrowRightFromLine className="mr-2 h-4 w-4" />
                        <span>Open</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer"
                        onClick={() => copyEndpoint(fn.id)}
                      >
                        <Copy className="mr-2 h-4 w-4" />
                        <span>Copy endpoint</span>
                      </DropdownMenuItem>
                      <DropdownMenuItem
                        className="cursor-pointer text-destructive focus:text-destructive"
                        onClick={() => setPendingDelete(fn)}
                      >
                        <Trash className="mr-2 h-4 w-4" />
                        <span>Delete</span>
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>

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
