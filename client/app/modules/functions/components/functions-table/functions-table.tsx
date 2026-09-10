"use client";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { EllipsisVertical, ArrowRightFromLine, Trash, Zap } from "lucide-react";
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
import { formatDate, parseDateString, cn } from "@/lib/utils";
import { IFunctionSummary } from "../../types/function.types";
import { FunctionStatusChip } from "../function-status-chip";
import { DeleteFunctionDialog } from "../delete-function-dialog";

const FunctionsTableSkeleton = ({ length }: { length: number }) => (
  <>
    {Array.from({ length: 10 }).map((_, index) => (
      <TableRow key={index} className="border-0">
        <TableCell colSpan={length} className="rounded-lg border border-border bg-background p-4">
          <Skeleton className="h-12 w-full" />
        </TableCell>
      </TableRow>
    ))}
  </>
);

const FunctionsEmptyState = ({ onCreateFunction }: { onCreateFunction: () => void }) => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
      <Zap className="h-7 w-7" />
    </div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">Create your first function</h4>
    <p className="mt-2 max-w-md text-sm text-muted-foreground">
      Write JavaScript, deploy it, and invoke it over HTTP or from a workflow.
    </p>
    <Button className="mt-6" size="sm" onClick={onCreateFunction}>
      Create function
    </Button>
  </div>
);

type ModalState = { type: "delete" | null; fn: IFunctionSummary | null };

type FunctionsTableProps = {
  functions: IFunctionSummary[];
  isLoading: boolean;
  onCreateFunction: () => void;
};

export const FunctionsTable = ({ functions, isLoading, onCreateFunction }: FunctionsTableProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [modal, setModal] = useState<ModalState>({ type: null, fn: null });

  const columns: ColumnDef<IFunctionSummary>[] = [
    {
      id: "name",
      header: () => <div className="font-bold text-medium-emphasis">Name</div>,
      cell: (info) => (
        <div className="w-[220px] truncate font-semibold md:w-[280px]">
          <Link
            to={scoped(`functions/${info.row.original.id}`)}
            className="transition-colors hover:text-primary hover:underline"
          >
            {info.row.original.name}
          </Link>
          <p className="truncate font-mono text-xs font-normal text-muted-foreground">
            {info.row.original.id}
          </p>
        </div>
      ),
    },
    {
      id: "status",
      header: () => <div className="font-bold text-medium-emphasis">Status</div>,
      cell: (info) => (
        <div className="flex items-center gap-2">
          <FunctionStatusChip status={info.row.original.status} />
          {info.row.original.isDirty && info.row.original.status === "Live" && (
            <span className="text-xs text-muted-foreground">Unpublished changes</span>
          )}
        </div>
      ),
    },
    {
      id: "version",
      header: () => <div className="font-bold text-medium-emphasis">Version</div>,
      cell: (info) => (
        <div className="text-muted-foreground">
          {info.row.original.activeVersionNumber ? `v${info.row.original.activeVersionNumber}` : "-"}
        </div>
      ),
    },
    {
      id: "totalRuns",
      header: () => <div className="font-bold text-medium-emphasis">Runs</div>,
      cell: (info) => <div className="text-muted-foreground">{info.row.original.totalRuns}</div>,
    },
    {
      id: "lastRunAt",
      header: () => <div className="font-bold text-medium-emphasis">Last run</div>,
      cell: (info) => (
        <div className="whitespace-nowrap text-muted-foreground">
          {info.row.original.lastRunAt
            ? formatDate(parseDateString(info.row.original.lastRunAt))
            : "-"}
        </div>
      ),
    },
    {
      id: "action",
      header: () => <div className="font-bold text-medium-emphasis"></div>,
      cell: (info) => (
        <div className="flex items-center gap-4" onClick={(e) => e.stopPropagation()}>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="h-5 w-5 p-0">
                <EllipsisVertical width={20} height={20} />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                className="cursor-pointer"
                onClick={() => navigate(scoped(`functions/${info.row.original.id}`))}
              >
                <ArrowRightFromLine className="mr-2 h-4 w-4" />
                <span>Open</span>
              </DropdownMenuItem>
              <DropdownMenuItem
                className="cursor-pointer text-error"
                onClick={() => setModal({ type: "delete", fn: info.row.original })}
              >
                <Trash className="mr-2 h-4 w-4" />
                <span>Delete</span>
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      ),
    },
  ];

  const table = useReactTable({
    data: functions || [],
    columns,
    getCoreRowModel: getCoreRowModel(),
  });

  return (
    <>
      {!isLoading && !functions.length ? (
        <FunctionsEmptyState onCreateFunction={onCreateFunction} />
      ) : (
        <Table className="border-separate border-spacing-y-4">
          <TableHeader className="[&_tr]:border-0">
            <TableRow className="border-0">
              {table.getHeaderGroups().map((headerGroup) =>
                headerGroup.headers.map((header) => (
                  <TableHead key={header.id} className="px-6 pb-0 pt-2 text-base">
                    {header.isPlaceholder
                      ? null
                      : flexRender(header.column.columnDef.header, header.getContext())}
                  </TableHead>
                )),
              )}
            </TableRow>
          </TableHeader>
          <TableBody className="[&_tr:last-child]:border-0">
            {isLoading && <FunctionsTableSkeleton length={columns.length} />}
            {!isLoading &&
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  className="group cursor-pointer border-0 transition-colors"
                  onClick={() => navigate(scoped(`functions/${row.original.id}`))}
                >
                  {row.getVisibleCells().map((cell, index, cells) => (
                    <TableCell
                      key={cell.id}
                      className={cn(
                        "border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50",
                        index === 0 && "rounded-l-lg border-l",
                        index === cells.length - 1 && "rounded-r-lg border-r",
                      )}
                    >
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
          </TableBody>
        </Table>
      )}

      <DeleteFunctionDialog
        open={modal.type === "delete"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, fn: null });
        }}
        fn={modal.fn}
      />
    </>
  );
};
