import {
  useGetWorkflowExecutionById,
  useResumeWorkflowExecution,
  useWorkflow,
} from "@blocks-workflow/hooks";
import { Background, BackgroundVariant, ReactFlow } from "@xyflow/react";
import { useEffect, useState } from "react";
import { Loader2, Play, ScrollText } from "lucide-react";
import { Button } from "@/components/ui-kits/button/button";
import { ExecutionLogsPanel } from "./execution-logs-panel";
import {
  WorkflowEditorDefaultEdgeOptions,
  WorkflowEditorNodeTypes,
} from "../workflow-editor-nodes";
import { WorkflowEditorEdgeTypes } from "../workflow-editor-edges";
import {
  getStatusStyles,
  buildExecutedSubgraph,
} from "@blocks-workflow/utils/workflow-execution-editor.util";
import { NodeInspector } from "../node-inspector";
import { EditorFitConfig, WorkflowEditorControls } from "../workflow-editor-controls";

import { getStatusConfig, shownExecutionStatus, WorkflowExecutionStatus } from "../../utils/workflow-execution-list.util";
import { WorkflowExecutionMode } from "../../models/workflow.model";
import { WorkflowExecution } from "@blocks-workflow/types/workflow.service.type";
import { cn } from "@/lib/utils";

export const WorkflowExecutionEditor = ({
  execution,
}: {
  execution?: WorkflowExecution;
}) => {
  const id = execution?.id || "";
  const { setWorkflow, onNodeClick, selectedNode, deselectNode, setEditorMode, setExecutionMode } = useWorkflow();
  const { data: responseData, isFetched, isLoading } = useGetWorkflowExecutionById({
    executionId: id,
  });

  const data = responseData?.data;
  const status = data?.status ?? execution?.status;
  // What the badge says; `status` (as stored) still decides Resume, which the server allows only on Failed.
  const shownStatus = shownExecutionStatus(status, data?.nodeExecutions);

  // Resume: only a failed production run. Completed steps are not run again; only a function step skips its already-done items.
  const resume = useResumeWorkflowExecution();
  const [resumeError, setResumeError] = useState<string | null>(null);
  const canResume =
    status === WorkflowExecutionStatus.Failed && execution?.executionMode === WorkflowExecutionMode.Production;
  const onResume = () => {
    setResumeError(null);
    resume.mutate(
      { executionId: id },
      {
        onSuccess: (result) => {
          if (!result?.isSuccess) setResumeError(result?.error ?? "This run cannot be resumed.");
        },
        onError: () => setResumeError("This run cannot be resumed."),
      },
    );
  };

  // At most one right-side panel: opening the logs closes the Node Inspector, and selecting a node closes the logs.
  const [logsOpen, setLogsOpen] = useState(false);
  const toggleLogs = () => {
    if (!logsOpen) deselectNode();
    setLogsOpen(!logsOpen);
  };
  const [previousSelectedNode, setPreviousSelectedNode] = useState(selectedNode);
  if (selectedNode !== previousSelectedNode) {
    setPreviousSelectedNode(selectedNode);
    if (selectedNode) setLogsOpen(false);
  }

  useEffect(() => {
    setEditorMode("execution");
    if (execution) {
      setExecutionMode(execution.executionMode);
    }
  }, [setEditorMode, setExecutionMode, execution]);

  useEffect(() => {
    if (data?.workflowSnapshot && isFetched) {
      const workflowData = data.workflowSnapshot;

      // Build the actually-executed subgraph via BFS
      const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(
        workflowData.nodes,
        workflowData.edges,
        data.nodeExecutions,
        data.items,
      );

      const nodeExecutionMap = new Map(
        data.nodeExecutions.map((ne) => [ne.nodeId, ne]),
      );

      // Style nodes — only colour nodes on the executed path
      workflowData.nodes.forEach((node) => {
        node.data = {
          ...node.data,
          isWorkflowExecuted: true,
          hasToolbar: false,
          hasHandleArrow: false,
        };

        if (reachableNodeIds.has(node.id)) {
          const execNode = nodeExecutionMap.get(node.id)!;
          const styles = getStatusStyles(execNode.status);
          node.className = styles.nodeClass;
          node.data.executionStatus = execNode.status;
        }
      });

      // Style edges — only colour edges that were actually traversed
      for (const edge of workflowData.edges) {
        if (traversedEdgeIds.has(edge.id)) {
          const sourceExec = nodeExecutionMap.get(edge.source);
          if (sourceExec) {
            const styles = getStatusStyles(sourceExec.status);
            edge.markerEnd = {
              type: "arrow",
              color: styles.edgeColor,
              height: 25,
              width: 25,
            };
            edge.className = styles.edgeClass;
            edge.style = { stroke: styles.edgeColor };
          }
        }
      }

      workflowData.items = data.items;
      workflowData.nodeExecutions = data.nodeExecutions;
      setWorkflow(workflowData);
    }
  }, [data, isFetched, setWorkflow]);

  if (!id) {
    return (
      <div className="flex h-full w-full items-center justify-center text-muted-foreground">
        Select an execution to view details
      </div>
    );
  }
  if (isLoading) {
    return (
      <div className="flex h-full w-full items-center justify-center text-muted-foreground">
        <Loader2 className="h-10 w-10 animate-spin text-primary" />
      </div>
    );
  }
  return (
    <div className="relative h-full w-full flex-1">
      <ReactFlow
        nodes={data?.workflowSnapshot.nodes || []}
        edges={data?.workflowSnapshot.edges || []}
        // onNodesChange={onNodesChange}
        // onEdgesChange={onEdgesChange}
        // onConnect={onConnect}
        onNodeClick={onNodeClick}
        // isValidConnection={isValidConnection}
        nodeTypes={WorkflowEditorNodeTypes}
        defaultEdgeOptions={WorkflowEditorDefaultEdgeOptions}
        edgeTypes={WorkflowEditorEdgeTypes}
        className="bg-background"
        fitView={EditorFitConfig.fitView}
        fitViewOptions={EditorFitConfig.fitViewOptions}
      >
        <Background
          variant={BackgroundVariant.Cross}
          gap={15}
          size={1.2}
          className="bg-surface-app opacity-60"
        />
        <WorkflowEditorControls readonly />
      </ReactFlow>
      {execution && (
        <div className="absolute left-4 top-4 z-50 flex items-center gap-2">
          <div className="flex items-center gap-2 rounded-md border bg-background/95 px-3 py-2 shadow-sm backdrop-blur-sm">
            <span className="text-sm font-medium">Status:</span>
            <div className="flex items-center gap-1.5">
              <div
                className={cn(
                  "h-2 w-2 rounded-full",
                  getStatusConfig(shownStatus ?? execution.status).color,
                )}
              ></div>
              <span
                className={cn(
                  "text-sm font-medium",
                  getStatusConfig(shownStatus ?? execution.status).textClass,
                )}
              >
                {getStatusConfig(shownStatus ?? execution.status).label}
              </span>
            </div>
          </div>
          <Button
            variant="outline"
            size="sm"
            className="gap-2 bg-background/95"
            onClick={toggleLogs}
            aria-pressed={logsOpen}
            data-testid="execution-logs-button"
          >
            <ScrollText className="h-4 w-4" /> Execution Logs
          </Button>
          {canResume && (
            <Button
              variant="outline"
              size="sm"
              className="gap-2 bg-background/95"
              onClick={onResume}
              disabled={resume.isPending}
              title="Continue from the failed step. Finished steps are not run again. Function steps also skip items that already succeeded; other steps run all their items again."
              data-testid="execution-resume-button"
            >
              {resume.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />} Resume
            </Button>
          )}
          {resumeError && (
            <span className="rounded-md bg-background/95 px-2 py-1 text-sm text-destructive" role="alert">
              {resumeError}
            </span>
          )}
        </div>
      )}
      {selectedNode && <NodeInspector key={selectedNode.id} />}
      {execution && (
        <ExecutionLogsPanel
          execution={execution}
          nodeExecutions={data?.nodeExecutions ?? []}
          open={logsOpen}
          onOpenChange={setLogsOpen}
        />
      )}
    </div>
  );
};
