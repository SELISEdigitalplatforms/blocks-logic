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
import { Copy, EllipsisVertical, ArrowRightFromLine, Ban, Trash, Check } from "lucide-react";
import { DeleteWorkflow } from "../delete-workflow";
import { DuplicateWorkflow } from "../duplicate-workflow";
import { Link, useNavigate } from "react-router-dom";
import { usePublishNewWorkflow, usePublishWorkflow, useUnpublishWorkflow } from "../../hooks/use-workflow-api";
import { PublishConfirmationModal, UnpublishConfirmationModal } from "../workflow-confirmation-modals";
import { PublishWorkflowModal } from "../publish-workflow-modal/publish-workflow-modal";
import { showErrorToast, showSuccessToast } from "@/hooks/use-toast";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui-kits/tooltip/tooltip";
import { useScopedPath } from "@seliseblocks/blocks-kit";


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
    type: "delete" | "publish" | "publish_new" | "unpublish" | "duplicate" | null;
    data: Record<string, unknown>;
  }>({
    type: null,
    data: {},
  });
  const scoped = useScopedPath();

  const [publishVersionName, setPublishVersionName] = useState("");
  const [publishDescription, setPublishDescription] = useState("");

  const { mutateAsync: publishNewWorkflow, isPending: isPublishingNew } = usePublishNewWorkflow();
  const { mutateAsync: publishWorkflow, isPending: isPublishingUnversioned } = usePublishWorkflow();
  const { mutateAsync: unpublishWorkflow, isPending: isUnpublishing } = useUnpublishWorkflow();

  const handlePublishNew = async () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    try {
      await publishNewWorkflow({ 
        workflowId, 
        name: publishVersionName, 
        description: publishDescription 
      });
      setModal({ type: null, data: {} });
      showSuccessToast({ description: "Workflow successfully published." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handlePublishUnversioned = async () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    try {
      await publishWorkflow({ workflowId });
      setModal({ type: null, data: {} });
      showSuccessToast({ description: "Workflow successfully published." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to publish workflow." });
    }
  };

  const handleUnpublish = async () => {
    const workflowId = modal.data.id as string;
    if (!workflowId) return;
    try {
      await unpublishWorkflow({ workflowId });
      setModal({ type: null, data: {} });
      showSuccessToast({ description: "Workflow successfully unpublished." });
    } catch (error: any) {
      showErrorToast({ errors: error.message || "Failed to unpublish workflow." });
    }
  };
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
        id: "isPublished",
        header: () => <div className="font-bold text-medium-emphasis">Status</div>,
        cell: (info) => (
          <div className="ml-2 sm:ml-0">
            <Badge
              variant={info.row.original.isPublished ? "success" : "secondary"}
              className="w-fit rounded-full"
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
          <div className="ml-2 flex items-center gap-4 sm:ml-0">
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
    [],
  );

  const handleRowClick = useCallback(
    (itemId: number | string) => {
      navigate(scoped(`workflow/${itemId}`));
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
          <TableRow>
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
    </>
  );
};
