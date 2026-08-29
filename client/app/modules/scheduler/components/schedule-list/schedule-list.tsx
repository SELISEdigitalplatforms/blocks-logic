"use client";
import { useCallback, useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath } from "@seliseblocks/genesis-os";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { CalendarClock, EllipsisVertical, ArrowRightFromLine, Pen, Trash } from "lucide-react";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { cn, formatDate, parseDateString } from "@/lib/utils";
import { isErrorWithErrors } from "@/lib/error";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useUpdateSchedule } from "../../hooks/use-schedule-api";
import { ISchedule, ScheduleKind } from "../../types/schedule.service.type";
import { DeleteScheduleDialog } from "../delete-schedule";

const ScheduleListSkeleton = ({ length }: { length: number }) => {
  return (
    <>
      {Array.from({ length: 10 }).map((_, index) => (
        <TableRow key={index} className="border-0">
          <TableCell
            colSpan={length}
            className="rounded-lg border border-border bg-background p-4"
          >
            <Skeleton className="h-12 w-full" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
};

type ModalState = {
  type: "delete" | null;
  schedule: ISchedule | null;
};

type ScheduleListProps = {
  schedules: ISchedule[];
  isLoading: boolean;
  onCreateSchedule: () => void;
};

const ScheduleEmptyState = ({ onCreateSchedule }: { onCreateSchedule: () => void }) => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
      <CalendarClock className="h-7 w-7" />
    </div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">
      Create your first schedule
    </h4>
    <p className="mt-2 max-w-md text-sm text-muted-foreground">
      Set up recurring runs with cron timing, payloads, and activation controls.
    </p>
    <Button className="mt-6" size="sm" onClick={onCreateSchedule}>
      Create schedule
    </Button>
  </div>
);

export const ScheduleList = ({
  schedules,
  isLoading,
  onCreateSchedule,
}: ScheduleListProps) => {
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [modal, setModal] = useState<ModalState>({ type: null, schedule: null });
  const { mutateAsync: updateScheduleAsync, isPending: isUpdating } =
    useUpdateSchedule();

  const handleToggleActive = useCallback(
    async (schedule: ISchedule, next: boolean) => {
      try {
        const res = await updateScheduleAsync({
          itemId: schedule.itemId,
          name: schedule.name ?? "Schedule",
          description: schedule.description ?? null,
          payload: schedule.payload,
          cronExpression: schedule.cronExpression,
          startDate: schedule.startDate ?? null,
          endDate: schedule.endDate ?? null,
          isActive: next,
          webhook: schedule.webhook,
        });
        if (!res.isSuccess) return showErrorToast({ errors: res.errors });
        showSuccessToast({
          description: next ? "Schedule activated." : "Schedule deactivated.",
        });
      } catch (error) {
        if (isErrorWithErrors(error))
          return showErrorToast({ errors: error.errors });
        return showErrorToast({ errors: "Failed to update schedule" });
      }
    },
    [updateScheduleAsync],
  );

  const columns: ColumnDef<ISchedule>[] = [
    {
      id: "name",
      header: () => <div className="font-bold text-medium-emphasis">Name</div>,
      cell: (info) => (
        <div
          className="w-[200px] truncate font-semibold md:w-[240px]"
          title={info.row.original.description ?? undefined}
        >
          <Link
            to={scoped(`schedule/${info.row.original.itemId}`)}
            className="hover:underline hover:text-primary transition-colors"
          >
            {info.row.original.name?.trim() || "-"}
          </Link>

          {info.row.original.kind === ScheduleKind.Internal && (
            <Badge variant="info" className="ml-2 inline-flex rounded-md px-2 py-0.5">
              Workflow
            </Badge>
          )}
          {info.row.original.description && (
            <p className="truncate text-xs text-muted-foreground font-normal">
              {info.row.original.description}
            </p>
          )}
        </div>
      ),
    },
    {
      id: "cronExpression",
      header: () => <div className="font-bold text-medium-emphasis">Cron Expression</div>,
      cell: (info) => (
        <div className="font-mono text-sm text-muted-foreground">
          {info.row.original.cronExpression || "-"}
        </div>
      ),
    },
    {
      id: "startDate",
      header: () => <div className="font-bold text-medium-emphasis">Start date</div>,
      cell: (info) => (
        <div className="whitespace-nowrap text-muted-foreground">
          {info.row.original.startDate
            ? formatDate(parseDateString(info.row.original.startDate))
            : "-"}
        </div>
      ),
    },
    {
      id: "endDate",
      header: () => <div className="font-bold text-medium-emphasis">End date</div>,
      cell: (info) => (
        <div className="whitespace-nowrap text-muted-foreground">
          {info.row.original.endDate
            ? formatDate(parseDateString(info.row.original.endDate))
            : "-"}
        </div>
      ),
    },
    {
      id: "isActive",
      header: () => <div className="font-bold text-medium-emphasis">Status</div>,
      cell: (info) => (
        <div>
          <Badge
            variant={info.row.original.isActive ? "success" : "error"}
            className="w-fit rounded-md px-3 py-1 text-sm"
          >
            {info.row.original.isActive ? "Active" : "Inactive"}
          </Badge>
        </div>
      ),
    },
    {
      id: "action",
      header: () => <div className="font-bold text-medium-emphasis"></div>,
      cell: (info) => {
        const isInternal = info.row.original.kind === ScheduleKind.Internal;
        return (
          <div
            className="flex items-center gap-4"
            onClick={(e) => e.stopPropagation()}
          >
            {/* <Switch
              size="md"
              checked={info.row.original.isActive}
              disabled={isUpdating || isInternal}
              onCheckedChange={(next) => handleToggleActive(info.row.original, next)}
            /> */}
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-5 w-5 p-0" disabled={isInternal}>
                  <EllipsisVertical width={20} height={20} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() => navigate(scoped(`schedule/${info.row.original.itemId}`))}
                >
                  <ArrowRightFromLine className="mr-2 h-4 w-4" />
                  <span>Open</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() => navigate(scoped(`schedule/${info.row.original.itemId}/edit`))}
                >
                  <Pen className="mr-2 h-4 w-4" />
                  <span>Edit</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer text-error"
                  onClick={() =>
                    setModal({ type: "delete", schedule: info.row.original })
                  }
                >
                  <Trash className="mr-2 h-4 w-4" />
                  <span>Delete</span>
                </DropdownMenuItem>
              </DropdownMenuContent>

            </DropdownMenu>
          </div>
        );
      },
    },
  ];

  const table = useReactTable({
    data: schedules || [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
  });

  return (
    <>
      {!isLoading && !schedules.length ? (
        <ScheduleEmptyState onCreateSchedule={onCreateSchedule} />
      ) : (
        <Table className="border-separate border-spacing-y-4">
          <TableHeader className="[&_tr]:border-0">
            <TableRow className="border-0">
              {table
                .getHeaderGroups()
                .map((headerGroup) =>
                  headerGroup.headers.map((header) => (
                    <TableHead key={header.id} className="px-6 pb-0 pt-2 text-base">
                      {header.isPlaceholder
                        ? null
                        : flexRender(
                            header.column.columnDef.header,
                            header.getContext(),
                          )}
                    </TableHead>
                  )),
                )}
            </TableRow>
          </TableHeader>
          <TableBody className="[&_tr:last-child]:border-0">
            {isLoading && <ScheduleListSkeleton length={columns.length} />}
            {!isLoading &&
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  className="group border-0 cursor-pointer transition-colors"
                  onClick={() => navigate(scoped(`schedule/${row.original.itemId}`))}
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

      <DeleteScheduleDialog
        open={modal.type === "delete"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, schedule: null });
        }}
        schedule={modal.schedule}
      />
    </>
  );
};
