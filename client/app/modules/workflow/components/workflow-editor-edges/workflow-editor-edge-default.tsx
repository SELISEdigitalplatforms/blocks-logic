import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from "@xyflow/react";
import { getHandleLabel } from "@blocks-workflow/constants";
import { useWorkflowStore } from "../../store";
import { getStatusStyles } from "../../utils/workflow-execution-editor.util";

export const WorkflowEditorEdgeDefault = ({
  id,
  source,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  markerEnd,
  style,
  sourceHandleId,
  selected,
}: EdgeProps) => {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    targetX,
    targetY,
    sourcePosition,
    targetPosition,
  });

  const stepExecutionTraversedEdgeIds = useWorkflowStore(s => s.stepExecutionTraversedEdgeIds);
  const executedNodes = useWorkflowStore(s => s.executedNodes);
  const isTraversed = stepExecutionTraversedEdgeIds?.has(id);
  const sourceNodeStatus = isTraversed ? executedNodes.find(n => n.nodeId === source)?.status : undefined;
  const executionStyles = sourceNodeStatus ? getStatusStyles(sourceNodeStatus) : undefined;

  const label = getHandleLabel(sourceHandleId);

  return (
    <>
      <BaseEdge 
        id={id} 
        path={edgePath} 
        markerEnd={markerEnd} 
        className={executionStyles?.edgeClass}
        style={{
          ...style,
          strokeWidth: style?.strokeWidth || 1,
          stroke: executionStyles?.edgeColor || (selected ? "hsl(var(--primary))" : style?.stroke),
        }} 
      />

      {label && (
        <EdgeLabelRenderer>
          <div
            style={{
              position: "absolute",
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
              pointerEvents: "all",
            }}
            className="rounded-md border border-medium-emphasis bg-background px-1.5 py-0.5 text-[10px] tracking-wider text-medium-emphasis shadow-sm dark:border-accent dark:text-primary"
          >
            {label}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  );
};
