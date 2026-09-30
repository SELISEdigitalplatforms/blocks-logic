"use client";

import { useLayoutEffect, useRef, useState } from "react";
import { useWorkflowExecutionHistory } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutionList } from "../workflow-execution-list";
import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { WorkflowExecutionEditor } from "./workflow-execution-editor";

import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { useParams } from "react-router";

export const WorkflowExecutions = () => {
  const { id: workflowId } = useParams<{ id: string }>();
  const [selectedExecution, setSelectedExecution] = useState<
    WorkflowExecution | undefined
  >();
  const listRef = useRef<HTMLDivElement>(null);
  const prevHeightRef = useRef(0);
  const prevTopRef = useRef(0);
  const lastPrependRef = useRef(0);
  const seenWorkflowRef = useRef(workflowId);

  const { rows, hasMore, isLoading, isLoadingMore, loadMore, prependCount } =
    useWorkflowExecutionHistory(workflowId || "");

  if (seenWorkflowRef.current !== workflowId) {
    seenWorkflowRef.current = workflowId;
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
  };

  const openExecution =
    rows.find((execution) => execution.id === selectedExecution?.id) || selectedExecution;

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
            selectedExecutionId={selectedExecution?.id}
            onSelectExecution={handleSelectExecution}
            onLoadMore={loadMore}
          />
          <WorkflowExecutionEditor
            execution={openExecution}
            key={selectedExecution?.id}
          />
        </div>
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};
