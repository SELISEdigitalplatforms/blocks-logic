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
import { cn, formatDate, parseDateString } from "@/lib/utils";
import { Badge } from "@/components/ui-kits/badge/badge";
import { Switch } from "@/components/ui-kits/switch/switch";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui-kits/dropdown-menu/dropdown-menu";
import { Button } from "@/components/ui-kits/button/button";
import {
  ArrowRightFromLine,
  Ban,
  Check,
  Copy,
  EllipsisVertical,
  Trash,
  Workflow,
} from "lucide-react";
import { DeleteWorkflow } from "../delete-workflow";
import { DuplicateWorkflow } from "../duplicate-workflow";
import { Link, useNavigate } from "react-router";
import { useWorkflowActions } from "../../hooks/use-workflow-actions";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { useScopedPath } from "@seliseblocks/genesis-os";
import { RenameWorkflow } from "../rename-workflow/rename-workflow";
import { Pen } from "lucide-react";
import { AddWorkflow } from "../add-workflow";


const WorkflowListSkeleton = ({ length }: { length: number }) => {
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

const WorkflowEmptyState = () => (
  <div className="flex min-h-[320px] flex-col items-center justify-center px-6 py-12 text-center">
    <div className="flex h-14 w-14 items-center justify-center rounded-md bg-primary/10 text-primary">
      <Workflow className="h-7 w-7" />
    </div>
    <h4 className="mt-5 text-lg font-semibold text-high-emphasis">
      Create your first workflow
    </h4>
    <p className="mt-2 max-w-md text-sm text-muted-foreground">
      Start building an automation flow with triggers, actions, and publish controls.
    </p>
    <div className="mt-6">
      <AddWorkflow
        variant="default"
        label="Create workflow"
        hideLabelOnMobile={false}
        showIcon={false}
      />
    </div>
  </div>
);

type WorkflowListProps = {
  workflow: WorkflowSummary[];
  isLoading: boolean;
};

export const WorkflowList = ({ workflow, isLoading }: WorkflowListProps) => {
  const navigate = useNavigate();
  const [modal, setModal] = useState<{
    type: "delete" | "publish" | "publish_new" | "unpublish" | "duplicate" | "rename" | null;
    data: Record<string, unknown>;
  }>({
    type: null,
    data: {},
  });
  const scoped = useScopedPath();

  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const {
    handlePublishNew: publishNew,
    handlePublishUnversioned: publishUnversioned,
    handleUnpublish: unpublish,
    isPublishingNew,
    isPublishingUnversioned,
    isUnpublishing,
  } = useWorkflowActions();

  const handlePublishNew = () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    publishNew(workflowId, publishVersionName, publishDescription, () => {
      setModal({ type: null, data: {} });
    });
  };

  const handlePublishUnversioned = () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    publishUnversioned(workflowId, undefined, () => {
      setModal({ type: null, data: {} });
    });
  };

  const handleUnpublish = () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    unpublish(workflowId, () => {
      setModal({ type: null, data: {} });
    });
  };
  const columns = useMemo<ColumnDef<WorkflowSummary>[]>(
    () => [
      {
        id: "name",
        header: () => <div className="font-bold text-medium-emphasis">Name</div>,
        cell: (info) => (
          <div className="w-[180px] truncate font-semibold md:w-[240px]">
            {`${info.row.original.name || ""}`.trim() || "-"}
          </div>
        ),
      },
      {
        id: "createdDate",
        header: () => <div className="font-bold text-medium-emphasis">Created on</div>,
        cell: (info) => (
          <div className="whitespace-nowrap text-muted-foreground">
            {formatDate(parseDateString(info.row.original.createdDate))}
          </div>
        ),
      },
      {
        id: "lastUpdatedDate",
        header: () => <div className="font-bold text-medium-emphasis">Last updated</div>,
        cell: (info) => (
          <div className="whitespace-nowrap text-muted-foreground">
            {formatDate(parseDateString(info.row.original.lastUpdatedDate))}
          </div>
        ),
      },
      {
        id: "isPublished",
        header: () => <div className="font-bold text-medium-emphasis">Status</div>,
        cell: (info) => (
          <div>
            <Badge
              variant={info.row.original.isPublished ? "success" : "error"}
              className="w-fit rounded-md px-3 py-1 text-sm"
            >
              {info.row.original.isPublished ? "Published" : "Unpublished"}
            </Badge>
          </div>
        ),
      },
      {
        id: "action",
        header: () => <div className="font-bold text-medium-emphasis"></div>,
        cell: (info) => (
          <div className="flex items-center gap-4">
            <Tooltip>
              <TooltipTrigger asChild>
                <div>
                  <Switch
                    size="md"
                    checked={info.row.original.isPublished}
                    onClick={(e) => e.stopPropagation()}
                    onCheckedChange={(_value) => {
                      if (!info.row.original.isPublished) {
                        if (!info.row.original.isDirty) {
                          setModal({
                            type: "publish",
                            data: { id: info.row.original.itemId },
                          });
                        } else {
                          const id = Math.random().toString(16).substring(2, 10);
                          setPublishVersionName(`Version ${id}`);
                          setPublishDescription("");
                          setModal({
                            type: "publish_new",
                            data: { id: info.row.original.itemId },
                          });
                        }
                      } else {
                        setModal({
                          type: "unpublish",
                          data: {
                            id: info.row.original.itemId,
                          },
                        });
                      }
                    }}
                  />
                </div>
              </TooltipTrigger>
              <TooltipContent>
                <p>{info.row.original.isPublished ? "Unpublish workflow" : "Publish workflow"}</p>
              </TooltipContent>
            </Tooltip>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" className="h-5 w-5 p-0">
                  <EllipsisVertical width={20} height={20} />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem className="cursor-pointer">
                  <Link
                    to={scoped(`workflow/${info.row.original.itemId}`)}
                    className="flex w-full items-center"
                  >
                    <ArrowRightFromLine className="mr-2 h-4 w-4" />
                    <span>Open</span>
                  </Link>
                </DropdownMenuItem>
                <DropdownMenuItem
                  className="cursor-pointer"
                  onClick={(e) => {
                    e.stopPropagation();
                    setModal({
                      type: "rename",
                      data: { id: info.row.original.itemId, name: info.row.original.name },
                    });
                  }}
                >
                  <Pen className="mr-2 h-4 w-4" />
                  <span>Rename</span>
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

                {!(info.row.original.isPublished) && (<DropdownMenuItem
                  className="cursor-pointer"
                  disabled={info.row.original.isPublished || !info.row.original.isDirty}
                  onClick={(e) => {
                    e.stopPropagation();
                    if (!info.row.original.isPublished && !info.row.original.isDirty) {
                      setModal({
                        type: "publish",
                        data: { id: info.row.original.itemId },
                      });
                    } else {
                      const id = Math.random().toString(16).substring(2, 10);
                      setPublishVersionName(`Version ${id}`);
                      setPublishDescription("");
                      setModal({
                        type: "publish_new",
                        data: { id: info.row.original.itemId },
                      });
                    }
                  }}
                >
                  <Check className="mr-2 h-4 w-4" />
                  <span>Publish</span>
                </DropdownMenuItem>)}

                {info.row.original.isPublished && (<DropdownMenuItem
                  className="cursor-pointer"
                  disabled={!info.row.original.isPublished}
                  onClick={(e) => {
                    e.stopPropagation();
                    setModal({
                      type: "unpublish",
                      data: { id: info.row.original.itemId },
                    });
                  }}
                >
                  <Ban className="mr-2 h-4 w-4" />
                  <span>Unpublish</span>
                </DropdownMenuItem>)}

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
    [
      publishDescription,
      publishNew,
      publishVersionName,
      publishUnversioned,
      scoped,
      unpublish,
    ],
  );

  const handleRowClick = useCallback(
    (itemId: number | string) => {
      navigate(scoped(`workflow/${itemId}`));
    },
    [navigate, scoped],
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
      {!isLoading && !workflow.length ? (
        <WorkflowEmptyState />
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
                        : flexRender(header.column.columnDef.header, header.getContext())}
                    </TableHead>
                  )),
                )}
            </TableRow>
          </TableHeader>
          <TableBody className="[&_tr:last-child]:border-0">
            {isLoading && <WorkflowListSkeleton length={columns.length} />}
            {!isLoading &&
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  isHoverable
                  className="group border-0"
                  onClick={() => handleRowClick(row.original.itemId)}
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
      <DeleteWorkflow
        open={modal.type === "delete"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
      />
      <PublishWorkflowModal
        open={modal.type === "publish_new"}
        onOpenChange={(open) => {
          if (!open) setModal({ type: null, data: {} });
        }}
        publishVersionName={publishVersionName}
        setPublishVersionName={setPublishVersionName}
        publishDescription={publishDescription}
        setPublishDescription={setPublishDescription}
        onPublish={handlePublishNew}
        isPublishing={isPublishingNew}
      />
      <PublishConfirmationModal
        open={modal.type === "publish"}
        onOpenChange={(open) => {
          if (!open) setModal({ type: null, data: {} });
        }}
        onConfirm={handlePublishUnversioned}
        isPending={isPublishingUnversioned}
      />
      <UnpublishConfirmationModal
        open={modal.type === "unpublish"}
        onOpenChange={(open) => {
          if (!open) setModal({ type: null, data: {} });
        }}
        onConfirm={handleUnpublish}
        isPending={isUnpublishing}
      />
      <DuplicateWorkflow
        open={modal.type === "duplicate"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
        name={modal.data.name as string}
      />
      <RenameWorkflow
        key={modal.type === "rename" ? (modal.data.id as string) : "closed"}
        open={modal.type === "rename"}
        onOpenChange={(value) => {
          if (!value) setModal({ type: null, data: {} });
        }}
        workflowId={modal.data.id as string}
        initialName={modal.data.name as string}
      />
    </>
  );
};
