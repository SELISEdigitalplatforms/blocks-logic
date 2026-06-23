import {
  useGetWorkflowExecutionById,
  useWorkflow,
} from "@blocks-workflow/hooks";
import { Background, BackgroundVariant, ReactFlow } from "@xyflow/react";
import { useEffect } from "react";
import { Edit, Loader2 } from "lucide-react";
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
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { EditorFitConfig } from "../workflow-editor-controls";

export const WorkflowExecutionEditor = ({ id }: { id: string }) => {
  const { setWorkflow, onNodeClick, selectedNode } = useWorkflow();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  const { data, isFetched, isLoading } = useGetWorkflowExecutionById({
    executionId: id,
    projectKey: tenantId,
  });

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
          isWorkflowExecuted: true,
          hasToolbar: false,
          hasHandleArrow: false,
        };

        if (reachableNodeIds.has(node.id)) {
          const execNode = nodeExecutionMap.get(node.id)!;
          const styles = getStatusStyles(execNode.status);
          node.className = styles.nodeClass;
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
      </ReactFlow>
      {selectedNode && <NodeInspector key={selectedNode.id} />}
    </div>
  );
};
