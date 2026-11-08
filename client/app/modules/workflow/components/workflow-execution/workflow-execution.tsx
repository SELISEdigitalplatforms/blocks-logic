"use client";

import { useLayoutEffect, useRef, useState } from "react";
import {
  useGetWorkflowExecutionById,
  useWorkflowExecutionHistory,
} from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutionList } from "../workflow-execution-list";
import {
  IGetWorkflowExecutionById,
  WorkflowExecution,
} from "@blocks-workflow/types/workflow.service.type";
import { WorkflowExecutionEditor } from "./workflow-execution-editor";

import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { useParams, useSearchParams } from "react-router";
import { EXECUTION_ID_PARAM } from "../../constants/workflow-execution.constant";

/** A run fetched by id, as a list row; only when it belongs to this workflow. */
const toListRow = (
  detail: IGetWorkflowExecutionById | undefined,
  workflowId: string | undefined,
): WorkflowExecution | undefined => {
  if (!detail || !workflowId || detail.workflowId !== workflowId) return undefined;
  return {
    id: detail.id,
    workflowId,
    status: detail.status ?? 0,
    executionMode: detail.executionMode,
    startedAt: detail.startedAt ?? "",
    finishedAt: detail.finishedAt ?? "",
    duration: 0,
    triggeredBy: "",
    errorMessage: detail.errorMessage ?? "",
    attemptNumber: detail.attemptNumber,
  };
};

export const WorkflowExecutions = () => {
  const { id: workflowId } = useParams<{ id: string }>();
  const [searchParams, setSearchParams] = useSearchParams();
  const linkedExecutionId = searchParams.get(EXECUTION_ID_PARAM) ?? "";
  const [selectedExecution, setSelectedExecution] = useState<
    WorkflowExecution | undefined
  >();
  const listRef = useRef<HTMLDivElement>(null);
  const prevHeightRef = useRef(0);
  const prevTopRef = useRef(0);
  const lastPrependRef = useRef(0);
  const [seenWorkflow, setSeenWorkflow] = useState(workflowId);

  const { rows, hasMore, isLoading, isLoadingMore, loadMore, prependCount } =
    useWorkflowExecutionHistory(workflowId || "");
  // Same query as the editor's, so the linked run is fetched once. It opens a run that is not on the
  // loaded pages yet (an older run from a notification).
  const { data: linkedDetail } = useGetWorkflowExecutionById({
    executionId: linkedExecutionId,
  });

  if (seenWorkflow !== workflowId) {
    setSeenWorkflow(workflowId);
    setSelectedExecution(undefined);
  }

  useLayoutEffect(() => {
    const viewport = listRef.current?.querySelector<HTMLElement>(
      "[data-radix-scroll-area-viewport]",
    );
    if (!viewport) return;

    if (prependCount !== lastPrependRef.current && prevTopRef.current > 0) {
      const delta = viewport.scrollHeight - prevHeightRef.current;
      viewport.scrollTop = prevTopRef.current + delta;
    }
    lastPrependRef.current = prependCount;
    prevHeightRef.current = viewport.scrollHeight;
    prevTopRef.current = viewport.scrollTop;
  });

  const handleSelectExecution = (execution: WorkflowExecution) => {
    setSelectedExecution(execution);
    setSearchParams(
      (prev) => {
        const next = new URLSearchParams(prev);
        next.set(EXECUTION_ID_PARAM, execution.id);
        return next;
      },
      { replace: true },
    );
  };

  const findOpenExecution = (): WorkflowExecution | undefined => {
    if (!linkedExecutionId) {
      return rows.find((execution) => execution.id === selectedExecution?.id) || selectedExecution;
    }
    const row = rows.find((execution) => execution.id === linkedExecutionId);
    if (row) return row;
    if (selectedExecution?.id === linkedExecutionId) return selectedExecution;
    return toListRow(linkedDetail?.data, workflowId);
  };
  const openExecution = findOpenExecution();

  return (
    <ReactFlowProvider>
      <WorkflowStoreProvider>
        <div className="flex h-full ">
          <WorkflowExecutionList
            ref={listRef}
            executions={rows}
            isLoading={isLoading}
            isLoadingMore={isLoadingMore}
            hasMore={hasMore}
            selectedExecutionId={openExecution?.id}
            onSelectExecution={handleSelectExecution}
            onLoadMore={loadMore}
          />
          <WorkflowExecutionEditor
            execution={openExecution}
            key={openExecution?.id}
          />
        </div>
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};
