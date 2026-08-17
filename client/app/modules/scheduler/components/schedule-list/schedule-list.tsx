"use client";
import { useCallback, useState } from "react";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { formatDate } from "date-fns";
import { EllipsisVertical, Pen, Trash } from "lucide-react";
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
import { parseDateString } from "@/lib/utils";
import { isErrorWithErrors } from "@/lib/error";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { useUpdateSchedule } from "../../hooks/use-schedule-api";
import {
  ISchedule,
  ScheduleTriggerType,
} from "../../types/schedule.service.type";
import { DeleteScheduleDialog } from "../delete-schedule";
import { ScheduleFormDialog } from "../schedule-form-dialog";

const ScheduleListSkeleton = ({ length }: { length: number }) => {
  return (
    <>
      {Array.from({ length: 10 }).map((_, index) => (
        <TableRow key={index} className="w-full border-b">
          <TableCell colSpan={length} className="py-4">
            <Skeleton className="aspect-square h-8 w-full" />
          </TableCell>
        </TableRow>
      ))}
    </>
  );
};

type ModalState = {
  type: "edit" | "delete" | null;
  schedule: ISchedule | null;
};

type ScheduleListProps = {
  schedules: ISchedule[];
  isLoading: boolean;
};

export const ScheduleList = ({ schedules, isLoading }: ScheduleListProps) => {
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
          webhook: schedule.webhook ,
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
          className="ml-2 w-[200px] truncate sm:ml-0 md:w-[240px]"
          title={info.row.original.description ?? undefined}
        >
          {info.row.original.name?.trim() || "-"}
          {info.row.original.description && (
            <p className="truncate text-xs text-muted-foreground">
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
        <div className="ml-2 font-mono text-sm sm:ml-0">
          {info.row.original.cronExpression || "-"}
        </div>
      ),
    },
    {
      id: "startDate",
      header: () => <div className="font-bold text-medium-emphasis">Start date</div>,
      cell: (info) => (
        <div className="ml-2 sm:ml-0">
          {info.row.original.startDate
            ? formatDate(parseDateString(info.row.original.startDate), "dd/MM/yyyy")
            : "-"}
        </div>
      ),
    },
    {
      id: "endDate",
      header: () => <div className="font-bold text-medium-emphasis">End date</div>,
      cell: (info) => (
        <div className="ml-2 sm:ml-0">
          {info.row.original.endDate
            ? formatDate(parseDateString(info.row.original.endDate), "dd/MM/yyyy")
            : "-"}
        </div>
      ),
    },
    {
      id: "isActive",
      header: () => <div className="font-bold text-medium-emphasis">Status</div>,
      cell: (info) => (
        <div className="ml-2 sm:ml-0">
          <Badge
            variant={info.row.original.isActive ? "success" : "secondary"}
            className="w-fit rounded-full"
          >
            {info.row.original.isActive ? "Active" : "Inactive"}
          </Badge>
        </div>
      ),
    },
    {
      id: "action",
      header: () => <div className="font-bold text-medium-emphasis"></div>,
      cell: (info) => (
        <div className="ml-2 flex items-center gap-4 sm:ml-0">
          <Switch
            size="md"
            checked={info.row.original.isActive}
            disabled={isUpdating}
            onCheckedChange={(next) => handleToggleActive(info.row.original, next)}
          />
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" className="h-5 w-5 p-0">
                <EllipsisVertical width={20} height={20} />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem
                className="cursor-pointer"
                onClick={() => setModal({ type: "edit", schedule: info.row.original })}
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
      ),
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
      <Table>
        <TableHeader>
          <TableRow>
            {table
              .getHeaderGroups()
              .map((headerGroup) =>
                headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
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
        <TableBody>
          {isLoading && <ScheduleListSkeleton length={columns.length} />}
          {!isLoading && (
            <>
              {!schedules.length ? (
                <TableRow>
                  <TableCell
                    colSpan={columns.length}
                    className="h-24 text-center text-muted-foreground"
                  >
                    No results found.
                  </TableCell>
                </TableRow>
              ) : (
                table.getRowModel().rows.map((row) => (
                  <TableRow key={row.id}>
                    {row.getVisibleCells().map((cell) => (
                      <TableCell key={cell.id}>
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </TableCell>
                    ))}
                  </TableRow>
                ))
              )}
            </>
          )}
        </TableBody>
      </Table>
      <ScheduleFormDialog
        open={modal.type === "edit"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, schedule: null });
        }}
        schedule={modal.schedule ?? undefined}
      />
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
