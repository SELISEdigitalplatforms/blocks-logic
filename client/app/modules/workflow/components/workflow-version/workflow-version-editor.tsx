import { useGetWorkflowByVersion, useWorkflow } from "@blocks-workflow/hooks";
import { Background, BackgroundVariant, ReactFlow } from "@xyflow/react";
import { useEffect } from "react";
import { Loader2 } from "lucide-react";
import {
  WorkflowEditorDefaultEdgeOptions,
  WorkflowEditorNodeTypes,
} from "../workflow-editor-nodes";
import { WorkflowEditorEdgeTypes } from "../workflow-editor-edges";
import { NodeInspector } from "../node-inspector";
import { useProjectStore } from "@seliseblocks/blocks-kit";
import { EditorFitConfig } from "../workflow-editor-controls";
import { useParams } from "react-router-dom";

export const WorkflowVersionEditor = ({ version }: { version?: any }) => {
  const { id: workflowId } = useParams<{ id: string }>();
  const versionId = version?.itemId || version?.id || "";
  const { setWorkflow, onNodeClick, selectedNode } = useWorkflow();
  const tenantId = useProjectStore().selectedProject?.tenantId || "";
  
  const { data, isFetched, isFetching } = useGetWorkflowByVersion({
    workflowId: workflowId || "",
    projectKey: tenantId,
    versionId,
  });

  useEffect(() => {
    if (data?.data && isFetched && versionId) {
      const snapshot = data.data;
      if (snapshot.nodes && Array.isArray(snapshot.nodes)) {
        snapshot.nodes.forEach((node: any) => {
          node.data = {
            ...node.data,
            isWorkflowExecuted: true,
            hasToolbar: false,
            hasHandleArrow: false,
          };
        });
      }
      setWorkflow(snapshot);
    }
  }, [data, isFetched, versionId, setWorkflow]);

  if (!versionId) {
    return (
      <div className="flex h-full w-full items-center justify-center text-muted-foreground">
        Select a version to view details
      </div>
    );
  }
  
  if (isFetching) {
    return (
      <div className="flex h-full w-full items-center justify-center text-muted-foreground">
        <Loader2 className="h-10 w-10 animate-spin text-primary" />
      </div>
    );
  }
  
  return (
    <div className="relative h-full w-full flex-1">
      <ReactFlow
        nodes={data?.data?.nodes || []}
        edges={data?.data?.edges || []}
        onNodeClick={onNodeClick}
        nodeTypes={WorkflowEditorNodeTypes}
        defaultEdgeOptions={WorkflowEditorDefaultEdgeOptions}
        edgeTypes={WorkflowEditorEdgeTypes}
        className="bg-background"
        fitView={EditorFitConfig.fitView}
        fitViewOptions={EditorFitConfig.fitViewOptions}
      >
        <Background
          variant={BackgroundVariant.Dots}
          gap={15}
          size={1.2}
          className="bg-surface-app opacity-60"
        />
      </ReactFlow>
      {version && (
        <div className="absolute left-4 top-4 z-50">
          <div className="flex items-center gap-2 rounded-md border bg-background/95 px-3 py-2 shadow-sm backdrop-blur-sm">
            <span className="text-sm font-medium">Version:</span>
            <span className="text-sm text-muted-foreground">
              {version?.name || "Unnamed Version"}
            </span>
          </div>
        </div>
      )}
      {selectedNode && <NodeInspector key={selectedNode.id} />}
    </div>
  );
};
