"use client";

import { useState } from "react";
import { useGetWorkflowExecutions } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutionList } from "../workflow-execution-list";
import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { useWorkflow } from "@blocks-workflow/hooks";
import { WorkflowExecutionEditor } from "./workflow-execution-editor";

import { ReactFlowProvider } from "@xyflow/react";
import { WorkflowStoreProvider } from "../../store";
import { useParams } from "react-router";

export const WorkflowExecutions = () => {
  const { id: workflowId } = useParams<{ id: string }>();
  const [selectedExecution, setSelectedExecution] = useState<
    WorkflowExecution | undefined
  >();

  const { data, isLoading } = useGetWorkflowExecutions({
    workflowId: workflowId || "",
  });

  const handleSelectExecution = (execution: WorkflowExecution) => {
    setSelectedExecution(execution);
  };

  return (
    <ReactFlowProvider>
      <WorkflowStoreProvider>
        <div className="flex h-full ">
          <WorkflowExecutionList
            executions={data?.data || []}
            isLoading={isLoading}
            selectedExecutionId={selectedExecution?.id}
            onSelectExecution={handleSelectExecution}
          />
          <WorkflowExecutionEditor
            execution={data?.data?.find(e => e.id === selectedExecution?.id) || selectedExecution}
            key={selectedExecution?.id}
          />
        </div>
      </WorkflowStoreProvider>
    </ReactFlowProvider>
  );
};
