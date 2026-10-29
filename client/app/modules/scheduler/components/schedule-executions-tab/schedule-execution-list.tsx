"use client";
import { KeyboardEvent, useMemo } from "react";
import { ColumnDef, flexRender, getCoreRowModel, useReactTable } from "@tanstack/react-table";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui-kits/table/table";
import { cn } from "@/lib/utils";
import { IScheduleExecutionListItem } from "../../types/schedule.service.type";
import { formatInZone } from "../../utils/timezone";
import { OutcomeBadge } from "./outcome-badge";
import { formatDuration, statusLabel } from "./status-label";
import { StatusChip } from "./status-chip";
import { ZonedTime } from "./zoned-time";

interface ScheduleExecutionListProps {
  executions: IScheduleExecutionListItem[];
  isLoading: boolean;
  tz: string;
  selectedId: string | null;
  onSelect: (id: string) => void;
}

const SKELETON_KEYS = Array.from({ length: 10 }, (_, row) => `schedule-execution-skeleton-${row + 1}`);

const ListSkeleton = ({ length }: Readonly<{ length: number }>) => (
  <>
    {SKELETON_KEYS.map((key) => (
      <TableRow key={key} className="border-0">
        <TableCell colSpan={length} className="rounded-lg border border-border bg-background p-4">
          <Skeleton className="h-12 w-full" />
        </TableCell>
      </TableRow>
    ))}
  </>
);

const ColumnHeader = ({ label }: Readonly<{ label: string }>) => (
  <div className="font-bold text-medium-emphasis">{label}</div>
);

const rowLabel = (row: IScheduleExecutionListItem, tz: string) =>
  `Open execution fired at ${row.firedUtc ? formatInZone(row.firedUtc, tz, "datetime") : "unknown time"}`;

export const ScheduleExecutionList = ({
  executions,
  isLoading,
  tz,
  selectedId,
  onSelect,
}: Readonly<ScheduleExecutionListProps>) => {
  const columns = useMemo<ColumnDef<IScheduleExecutionListItem>[]>(
    () => [
      { id: "firedUtc", header: () => <ColumnHeader label="Fired at" />, cell: (info) => <ZonedTime iso={info.row.original.firedUtc} tz={tz} /> },
      { id: "outcome", header: () => <ColumnHeader label="Result" />, cell: (info) => <OutcomeBadge outcome={info.row.original.outcome} /> },
      { id: "status", header: () => <ColumnHeader label="Status" />, cell: (info) => <StatusChip label={statusLabel(info.row.original.status)} /> },
      {
        id: "durationMs",
        header: () => <ColumnHeader label="Duration" />,
        cell: (info) => <span className="whitespace-nowrap">{formatDuration(info.row.original.durationMs)}</span>,
      },
    ],
    [tz],
  );

  // TanStack Table returns non-memoizable functions; the React compiler skips this component.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({ data: executions, columns, getCoreRowModel: getCoreRowModel() });

  const onKeyDown = (event: KeyboardEvent<HTMLTableRowElement>, id: string) => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    onSelect(id);
  };

  return (
    <Table className="border-separate border-spacing-y-4">
      <TableHeader className="[&_tr]:border-0">
        <TableRow className="border-0">
          {table.getFlatHeaders().map((h) => (
            <TableHead key={h.id} className="px-6 pb-0 pt-2 text-base">
              {flexRender(h.column.columnDef.header, h.getContext())}
            </TableHead>
          ))}
        </TableRow>
      </TableHeader>
      <TableBody className="[&_tr:last-child]:border-0">
        {isLoading && <ListSkeleton length={columns.length} />}
        {!isLoading &&
          table.getRowModel().rows.map((row) => (
            <TableRow
              key={row.original.id}
              isHoverable
              tabIndex={0}
              role="button"
              aria-label={rowLabel(row.original, tz)}
              className="group border-0 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              onClick={() => onSelect(row.original.id)}
              onKeyDown={(event) => onKeyDown(event, row.original.id)}
            >
              {row.getVisibleCells().map((cell, index, cells) => (
                <TableCell
                  key={cell.id}
                  className={cn(
                    "border-y border-border bg-background px-6 py-5 text-base transition-colors group-hover:bg-muted/50",
                    selectedId === row.original.id && "bg-muted/50",
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
  );
};
