import { EditorNodeBase } from "./editor-node-base";
import { Position, Node, useUpdateNodeInternals } from "@xyflow/react";
import { EditorNodeTitle } from "./editor-node-title";
import { useWorkflowStore } from "../../store";
import { EditorNodeHandle, EditorNodeHandleArrow } from "./editor-node-handle";
import { getNodeDefinition } from "../node-library-panel";
import { useEffect, useMemo, useRef } from "react";

import { WorkflowExecutionStatus, getStatusConfig } from "../../utils/workflow-execution-list.util";
import { cn } from "@/lib/utils";

/** One handle row: 10px dot plus the 16px gap between dots. */
const HANDLE_ROW_PX = 26;

/**
 * Inner min-height so every handle sits inside the card, or undefined for up to two handles per side,
 * which already fit and keep today's layout.
 */
export const handleColumnMinHeight = (count: number): number | undefined =>
  count > 2 ? count * HANDLE_ROW_PX - 16 : undefined;

export const EditorNodeSimple = ({ id }: Node) => {
  const node = useWorkflowStore((state) => state.nodesMap[id]);
  const editorMode = useWorkflowStore((state) => state.editorMode);
  const edgesMap = useWorkflowStore((state) => state.edgesMap);

  const nodeDefinition = useMemo(() => {
    if (!node) return null;
    return getNodeDefinition(node.category, node.type, node.version);
  }, [node]);

  const handle = node?.handle ?? nodeDefinition?.handle;
  const sources = handle?.sources ?? [];
  const targets = handle?.targets ?? [];
  const handleKeys = `${targets.map((entry) => entry.key).join("|")}>${sources.map((entry) => entry.key).join("|")}`;

  // xyflow measures handles once; when a node's handle keys change it must re-measure, or edges to the
  // new keys fail to draw. The first render is measured by xyflow itself.
  const updateNodeInternals = useUpdateNodeInternals();
  const measuredKeys = useRef(handleKeys);
  useEffect(() => {
    if (measuredKeys.current === handleKeys) return;
    measuredKeys.current = handleKeys;
    updateNodeInternals(id);
  }, [handleKeys, id, updateNodeInternals]);

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
  const minHeight = handleColumnMinHeight(Math.max(targets.length, sources.length));

  return (
    <EditorNodeBase id={id}>
      <div className="absolute -left-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {targets.map(({ key: handleId }) => {
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

      {minHeight === undefined ? (
        <EditorNodeTitle icon={nodeDefinition.icon} title={nodeDefinition.title} />
      ) : (
        <div className="flex items-center" style={{ minHeight }}>
          <EditorNodeTitle icon={nodeDefinition.icon} title={nodeDefinition.title} />
        </div>
      )}

      <div className="absolute -right-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {sources.map(({ key: handleId }) => {
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
