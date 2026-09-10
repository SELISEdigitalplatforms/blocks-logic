"use client";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { EllipsisVertical, ArrowRightFromLine, RotateCcw, Ban } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { formatDate, parseDateString } from "@/lib/utils";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { isErrorWithErrors } from "@/lib/error";
import { IRunSummary, TERMINAL_RUN_STATUSES } from "../../types/run.types";
import { RunStatusChip } from "../run-status-chip";
import { useCancelRun, useReplayRun } from "../../hooks/use-runs";

const RunsTableSkeleton = ({ length }: { length: number }) => (
  <>
    {Array.from({ length: 8 }).map((_, index) => (
      <TableRow key={index} className="border-0">
        <TableCell colSpan={length} className="rounded-lg border border-border bg-background p-4">
          <Skeleton className="h-10 w-full" />
        </TableCell>
      </TableRow>
    ))}
  </>
);

type RunsTableProps = {
  runs: IRunSummary[];
  isLoading: boolean;
  functionRoutePrefix: string;
};

export const RunsTable = ({ runs, isLoading, functionRoutePrefix }: RunsTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
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

  const columns: ColumnDef<IRunSummary>[] = [
    {
      id: "id",
      header: () => <div className="font-bold text-medium-emphasis">Run</div>,
      cell: (info) => (
        <Link
          to={scoped(`${functionRoutePrefix}?tab=runs&runId=${info.row.original.id}`)}
          className="font-mono text-xs hover:text-primary hover:underline"
        >
          {info.row.original.id.slice(0, 12)}…
        </Link>
      ),
    },
    {
      id: "status",
      header: () => <div className="font-bold text-medium-emphasis">Status</div>,
      cell: (info) => <RunStatusChip status={info.row.original.status} />,
    },
    {
      id: "invokedBy",
      header: () => <div className="font-bold text-medium-emphasis">Invoked by</div>,
      cell: (info) => <div className="text-muted-foreground">{info.row.original.invokedBy}</div>,
    },
    {
      id: "attempt",
      header: () => <div className="font-bold text-medium-emphasis">Attempt</div>,
      cell: (info) => <div className="text-muted-foreground">{info.row.original.attempt}</div>,
    },
    {
      id: "duration",
      header: () => <div className="font-bold text-medium-emphasis">Duration</div>,
      cell: (info) => (
        <div className="text-muted-foreground">
          {info.row.original.durationMs != null ? `${info.row.original.durationMs} ms` : "-"}
        </div>
      ),
    },
    {
      id: "createdDate",
      header: () => <div className="font-bold text-medium-emphasis">Created</div>,
      cell: (info) => (
        <div className="whitespace-nowrap text-muted-foreground">
          {formatDate(parseDateString(info.row.original.createdDate))}
        </div>
      ),
    },
    {
      id: "action",
      header: () => <div className="font-bold text-medium-emphasis"></div>,
      cell: (info) => {
        const isTerminal = TERMINAL_RUN_STATUSES.includes(info.row.original.status);
        return (
          <div onClick={(e) => e.stopPropagation()}>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-5 w-5 p-0">
                  <EllipsisVertical width={20} height={20} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() =>
                    navigate(scoped(`${functionRoutePrefix}?tab=runs&runId=${info.row.original.id}`))
                  }
                >
                  <ArrowRightFromLine className="mr-2 h-4 w-4" />
                  <span>Open</span>
                </DropdownMenuItem>
                {isTerminal && (
                  <DropdownMenuItem className="cursor-pointer" onClick={() => handleReplay(info.row.original.id)}>
                    <RotateCcw className="mr-2 h-4 w-4" />
                    <span>Replay</span>
                  </DropdownMenuItem>
                )}
                {!isTerminal && (
                  <DropdownMenuItem
                    className="cursor-pointer text-error"
                    onClick={() => handleCancel(info.row.original.id)}
                  >
                    <Ban className="mr-2 h-4 w-4" />
                    <span>Cancel</span>
                  </DropdownMenuItem>
                )}
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        );
      },
    },
  ];

  const table = useReactTable({ data: runs || [], columns, getCoreRowModel: getCoreRowModel() });

  return (
    <Table className="border-separate border-spacing-y-3">
      <TableHeader className="[&_tr]:border-0">
        <TableRow className="border-0">
          {table.getHeaderGroups().map((headerGroup) =>
            headerGroup.headers.map((header) => (
              <TableHead key={header.id} className="px-4 pb-0 pt-2 text-sm">
                {header.isPlaceholder
                  ? null
                  : flexRender(header.column.columnDef.header, header.getContext())}
              </TableHead>
            )),
          )}
        </TableRow>
      </TableHeader>
      <TableBody className="[&_tr:last-child]:border-0">
        {isLoading && <RunsTableSkeleton length={columns.length} />}
        {!isLoading && runs.length === 0 && (
          <TableRow className="border-0">
            <TableCell colSpan={columns.length} className="py-10 text-center text-sm text-muted-foreground">
              No runs yet.
            </TableCell>
          </TableRow>
        )}
        {!isLoading &&
          table.getRowModel().rows.map((row) => (
            <TableRow key={row.id} className="group border-0">
              {row.getVisibleCells().map((cell, index, cells) => (
                <TableCell
                  key={cell.id}
                  className={
                    "border-y border-border bg-background px-4 py-3 text-sm " +
                    (index === 0 ? "rounded-l-lg border-l " : "") +
                    (index === cells.length - 1 ? "rounded-r-lg border-r" : "")
                  }
                >
                  {flexRender(cell.column.columnDef.cell, cell.getContext())}
                </TableCell>
              ))}
            </TableRow>
          ))}
      </TableBody>
    </Table>
  );
};
