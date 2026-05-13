"use client";
import { Skeleton } from "@/components/ui-kits/skeleton/skeleton";
import { useCallback, useMemo, useState } from "react";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui-kits/table/table";
import {
  ColumnDef,
  flexRender,
  getCoreRowModel,
  getFacetedRowModel,
  getFacetedUniqueValues,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable,
} from "@tanstack/react-table";
import { WorkflowSummary } from "../../models/workflow.model";
import { parseDateString } from "@/lib/utils";
import { formatDate } from "date-fns";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import { Copy, EllipsisVertical, Pen, Ban, Trash, Check } from "lucide-react";
import { DeleteWorkflow } from "../delete-workflow";
import { ToggleStatusWorkflow } from "../toggle-status-workflow";
import { DuplicateWorkflow } from "../duplicate-workflow";
import { Link, useNavigate } from "react-router-dom";


const WorkflowListSkeleton = ({ length }: { length: number }) => {
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

type WorkflowListProps = {
  workflow: WorkflowSummary[];
  isLoading: boolean;
};

export const WorkflowList = ({ workflow, isLoading }: WorkflowListProps) => {
  const navigate = useNavigate();
  const [modal, setModal] = useState<{
    type: "delete" | "toggleStatus" | "duplicate" | null;
    data: Record<string, unknown>;
  }>({
    type: null,
    data: {},
  });
  const columns = useMemo<ColumnDef<WorkflowSummary>[]>(
    () => [
      {
        id: "name",
        header: () => <div className="font-bold text-medium-emphasis">Name</div>,
        cell: (info) => (
          <div className="ml-2 w-[180px] truncate sm:ml-0 md:w-[240px]">
            {`${info.row.original.name || ""}`.trim() || "-"}
          </div>
        ),
      },
      {
        id: "createdDate",
        header: () => <div className="font-bold text-medium-emphasis">Creation date</div>,
        cell: (info) => (
          <div className="ml-2 sm:ml-0">
            {formatDate(parseDateString(info.row.original.createdDate), "dd/MM/yyyy")}
          </div>
        ),
      },
      {
        id: "lastUpdatedDate",
        header: () => <div className="font-bold text-medium-emphasis">Last updated</div>,
        cell: (info) => (
          <div className="ml-2 sm:ml-0">
            {formatDate(parseDateString(info.row.original.lastUpdatedDate), "dd/MM/yyyy")}
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
              onClick={(e) => e.stopPropagation()}
              onCheckedChange={(_value) => {
                setModal({
                  type: "toggleStatus",
                  data: {
                    id: info.row.original.itemId,
                    isActive: info.row.original.isActive,
                  },
                });
              }}
            />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-5 w-5 p-0">
                  <EllipsisVertical width={20} height={20} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem className="cursor-pointer">
                  <Link
                    to={`/workflow/${info.row.original.itemId}`}
                    className="flex w-full items-center"
                  >
                    <Pen className="mr-2 h-4 w-4" />
                    <span>Open</span>
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={(e) => {
                    e.stopPropagation();
                    setModal({
                      type: "duplicate",
                      data: { id: info.row.original.itemId, name: info.row.original.name },
                    });
                  }}
                >
                  <Copy className="mr-2 h-4 w-4" />
                  <span>Duplicate</span>
                </DropdownMenuItem>

                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={(e) => {
                    e.stopPropagation();
                    setModal({
                      type: "toggleStatus",
                      data: { id: info.row.original.itemId, isActive: info.row.original.isActive },
                    });
                  }}
                >
                  {info.row.original.isActive ? (
                    <Ban className="mr-2 h-4 w-4" />
                  ) : (
                    <Check className="mr-2 h-4 w-4" />
                  )}
                  <span>{info.row.original.isActive ? "Deactivate" : "Activate"}</span>
                </DropdownMenuItem>

                <DropdownMenuItem
                  className="cursor-pointer text-error"
                  onClick={(e) => {
                    e.stopPropagation();
                    setModal({ type: "delete", data: { id: info.row.original.itemId } });
                  }}
                >
                  <Trash className="mr-2 h-4 w-4" />
                  <span>Delete</span>
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        ),
      },
    ],
    [],
  );

  const handleRowClick = useCallback(
    (itemId: number | string) => {
      navigate(`/workflow/${itemId}`);
    },
    [navigate],
  );

  const table = useReactTable({
    data: workflow || [],
    columns,
    enableRowSelection: true,
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFacetedRowModel: getFacetedRowModel(),
    getFacetedUniqueValues: getFacetedUniqueValues(),
  });

  return (
    <>
      <Table>
        <TableHeader>
          <TableRow isHoverable>
            {table
              .getHeaderGroups()
              .map((headerGroup) =>
                headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : flexRender(header.column.columnDef.header, header.getContext())}
                  </TableHead>
                )),
              )}
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading && <WorkflowListSkeleton length={columns.length} />}
          {!isLoading && (
            <>
              {!workflow.length ? (
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
                  <TableRow
                    key={row.id}
                    isHoverable
                    onClick={() => handleRowClick(row.original.itemId)}
                  >
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
      <DeleteWorkflow
        open={modal.type === "delete"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
      />
      <ToggleStatusWorkflow
        open={modal.type === "toggleStatus"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
        isActive={modal.data.isActive as boolean}
      />
      <DuplicateWorkflow
        open={modal.type === "duplicate"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
        name={modal.data.name as string}
      />
    </>
  );
};
