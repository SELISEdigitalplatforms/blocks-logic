"use client";

import { useState } from "react";
import { useGetWorkflowExecutions } from "@blocks-workflow/hooks/use-workflow-api";
import { WorkflowExecutionList } from "../workflow-execution-list";
import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { useWorkflow } from "@blocks-workflow/hooks";
import { WorkflowExecutionEditor } from "./workflow-execution-editor";
import { useProjectStore } from "@/store/useProjectStore";

export const WorkflowExecutions = () => {
  const { workflowId } = useWorkflow();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const [selectedExecution, setSelectedExecution] = useState<
    WorkflowExecution | undefined
  >();

  const { data, isLoading } = useGetWorkflowExecutions({
    projectKey: tenantId,
    workflowId: workflowId || "",
  });

  const handleSelectExecution = (execution: WorkflowExecution) => {
    setSelectedExecution(execution);
  };

  return (
    <div className="flex h-full">
      <WorkflowExecutionList
        executions={data?.data || []}
        isLoading={isLoading}
        selectedExecutionId={selectedExecution?.id}
        onSelectExecution={handleSelectExecution}
      />
      <WorkflowExecutionEditor
        id={selectedExecution?.id || ""}
        key={selectedExecution?.id}
      />
    </div>
  );
};
