import { EditorNodeBase } from "./editor-node-base";
import { Position, Node } from "@xyflow/react";
import { EditorNodeTitle } from "./editor-node-title";
import { useWorkflowStore } from "../../store";
import { EditorNodeHandle, EditorNodeHandleArrow } from "./editor-node-handle";
import { getNodeDefinition } from "../node-library-panel";
import { useMemo } from "react";

import { WorkflowExecutionStatus, getStatusConfig } from "../../utils/workflow-execution-list.util";
import { cn } from "@/lib/utils";

export const EditorNodeSimple = ({ id }: Node) => {
  const node = useWorkflowStore((state) => state.nodesMap[id]);
  const editorMode = useWorkflowStore((state) => state.editorMode);
  const edgesMap = useWorkflowStore((state) => state.edgesMap);

  const nodeDefinition = useMemo(() => {
    if (!node) return null;
    return getNodeDefinition(node.category, node.type, node.version);
  }, [node]);

  const { incoming, outgoing } = useMemo(() => {
    const edges = Object.values(edgesMap);
    return {
      incoming: edges.filter((edge) => edge.target === id),
      outgoing: edges.filter((edge) => edge.source === id),
    };
  }, [edgesMap, id]);

  if (!node) return null;
  if (!nodeDefinition) return null;

  const executionStatus = node.data?.executionStatus as WorkflowExecutionStatus | undefined;
  let StatusIcon = null;
  let statusConfig = null;
  if (executionStatus !== undefined) {
    statusConfig = getStatusConfig(executionStatus);
    StatusIcon = statusConfig.icon;
  }

  const isReadOnlyMode = editorMode === "execution" || editorMode === "version";

  return (
    <EditorNodeBase id={id}>
      <div className="absolute -left-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.target.map((handleId) => {
          const isConnected = incoming.some(
            (edge) => edge.targetHandle === handleId || (!edge.targetHandle && handleId === "target")
          );
          if (isReadOnlyMode && !isConnected) return null;

          return (
            <EditorNodeHandle
              key={handleId}
              type="target"
              position={Position.Left}
              id={handleId}
              nodeId={node.id}
            ></EditorNodeHandle>
          );
        })}
      </div>

      <EditorNodeTitle icon={nodeDefinition.icon} title={nodeDefinition.title} />

      <div className="absolute -right-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.source.map((handleId) => {
          const isConnected = outgoing.some(
            (edge) => edge.sourceHandle === handleId || (!edge.sourceHandle && handleId === "source")
          );
          if (isReadOnlyMode && !isConnected) return null;

          return (
            <EditorNodeHandle
              key={handleId}
              type="source"
              position={Position.Right}
              id={handleId}
              nodeId={node.id}
              className="relative right-auto top-auto transform-none"
            >
              <EditorNodeHandleArrow />
            </EditorNodeHandle>
          );
        })}
      </div>
      {StatusIcon && statusConfig && (
        <div className="absolute -bottom-2 -right-2 z-10 flex h-6 w-6 items-center justify-center rounded-full bg-background shadow-sm border">
            <StatusIcon className={cn("h-4 w-4", statusConfig.textClass, executionStatus === WorkflowExecutionStatus.Running && "animate-spin")} />
        </div>
      )}
    </EditorNodeBase>
  );
};
