"use client";
import { useState } from "react";
import { Link, useNavigate } from "react-router";
import { useScopedPath, useT } from "@seliseblocks/genesis-os";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getPaginationRowModel,
  useReactTable,
} from "@tanstack/react-table";
import {
  CalendarClock,
  EllipsisVertical,
  ArrowRightFromLine,
  Pen,
  Search,
  Trash,
} from "lucide-react";
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
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { cn, formatDate, parseDateString } from "@/lib/utils";
import { ISchedule, ScheduleKind } from "../../types/schedule.service.type";
import { DeleteScheduleDialog } from "../delete-schedule";

const LOGIC_SCHEDULER_MODULE = "logic.scheduler";

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
  /** True when a search term is narrowing the list, so a zero-row result is "no matches" rather
   * than "nothing has ever been created" — the two need different copy. */
  isFiltered?: boolean;
};

const ScheduleEmptyState = ({ onCreateSchedule }: { onCreateSchedule: () => void }) => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  return (
    <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
        <CalendarClock className="h-7 w-7" />
      </div>
      <h4 className="mt-5 text-lg font-semibold text-high-emphasis">
        {t("SCHEDULE_LIST.CREATE_YOUR_FIRST_SCHEDULE")}
      </h4>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        {t("SCHEDULE_LIST.SET_UP_RECURRING_RUNS")}
      </p>
      <Button className="mt-6" size="sm" onClick={onCreateSchedule}>
        {t("SCHEDULE_LIST.CREATE_SCHEDULE")}
      </Button>
    </div>
  );
};

const ScheduleNoResultsState = () => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  return (
    <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
      <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
        <Search className="h-7 w-7" />
      </div>
      <h4 className="mt-5 text-lg font-semibold text-high-emphasis">{t("SCHEDULE_LIST.NO_SCHEDULES_FOUND")}</h4>
      <p className="mt-2 max-w-md text-sm text-muted-foreground">
        {t("SCHEDULE_LIST.NO_SCHEDULES_MATCH_YOUR")}
      </p>
    </div>
  );
};

export const ScheduleList = ({
  schedules,
  isLoading,
  onCreateSchedule,
  isFiltered,
}: ScheduleListProps) => {
  const { t } = useT(LOGIC_SCHEDULER_MODULE);
  const navigate = useNavigate();
  const scoped = useScopedPath();
  const [modal, setModal] = useState<ModalState>({ type: null, schedule: null });

  const columns: ColumnDef<ISchedule>[] = [
    {
      id: "name",
      header: () => <div className="font-bold text-medium-emphasis">{t("common:NAME")}</div>,
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
              {t("SCHEDULE_LIST.WORKFLOW")}
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
      header: () => <div className="font-bold text-medium-emphasis">{t("SCHEDULE_LIST.CRON_EXPRESSION")}</div>,
      cell: (info) => (
        <div className="font-mono text-sm text-muted-foreground">
          {info.row.original.cronExpression || "-"}
        </div>
      ),
    },
    {
      id: "startDate",
      header: () => <div className="font-bold text-medium-emphasis">{t("SCHEDULE_LIST.START_DATE")}</div>,
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
      header: () => <div className="font-bold text-medium-emphasis">{t("SCHEDULE_LIST.END_DATE")}</div>,
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
      header: () => <div className="font-bold text-medium-emphasis">{t("common:STATUS")}</div>,
      cell: (info) => (
        <div>
          <Badge
            variant={info.row.original.isActive ? "success" : "error"}
            className="w-fit rounded-md px-3 py-1 text-sm"
          >
            {info.row.original.isActive ? t("SCHEDULE_LIST.ACTIVE") : t("SCHEDULE_LIST.INACTIVE")}
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
                  <span>{t("SCHEDULE_LIST.OPEN")}</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={() => navigate(scoped(`schedule/${info.row.original.itemId}/edit`))}
                >
                  <Pen className="mr-2 h-4 w-4" />
                  <span>{t("common:EDIT")}</span>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer text-error"
                  onClick={() =>
                    setModal({ type: "delete", schedule: info.row.original })
                  }
                >
                  <Trash className="mr-2 h-4 w-4" />
                  <span>{t("common:DELETE")}</span>
                </DropdownMenuItem>
              </DropdownMenuContent>

            </DropdownMenu>
          </div>
        );
      },
    },
  ];

  // TanStack Table returns non-memoizable functions; the React compiler skips this component
  // either way and there is no compatible alternative API.
  // eslint-disable-next-line react-hooks/incompatible-library
  const table = useReactTable({
    data: schedules || [],
    columns,
    getCoreRowModel: getCoreRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
  });

  return (
    <>
      {!isLoading && !schedules.length ? (
        isFiltered ? (
          <ScheduleNoResultsState />
        ) : (
          <ScheduleEmptyState onCreateSchedule={onCreateSchedule} />
        )
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
