import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from "@xyflow/react";
import { getHandleLabel } from "@blocks-workflow/constants";
import { useWorkflow } from "../../hooks";
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

  const { stepExecutionTraversedEdgeIds, executedNodes } = useWorkflow();
  const isTraversed = stepExecutionTraversedEdgeIds?.has(id);
  const executedNode = isTraversed ? executedNodes.find(n => n.nodeId === source) : undefined;
  const sourceNodeStatus = executedNode?.status;
  const executionStyles = sourceNodeStatus ? getStatusStyles(sourceNodeStatus) : undefined;

  const label = getHandleLabel(sourceHandleId);

  let itemCountLabel = "";
  if (executedNode) {
    let count = 0;
    const isBranchingEdge = sourceHandleId && sourceHandleId !== "source";
    if (isBranchingEdge) {
      const branchCounts = executedNode.outputCountsByBranch;
      if (branchCounts) {
        const mappedKey =
          sourceHandleId === "if-true"
            ? "True"
            : sourceHandleId === "if-false"
              ? "False"
              : sourceHandleId;
        if (mappedKey && mappedKey in branchCounts) {
          count = branchCounts[mappedKey];
        } else if (sourceHandleId && sourceHandleId in branchCounts) {
          count = branchCounts[sourceHandleId];
        }
      }
    } else if (executedNode.outputItemCount !== undefined) {
      count = executedNode.outputItemCount;
    }

    if (count > 0) {
      itemCountLabel = `${count} item${count > 1 ? "s" : ""}`;
    }
  }

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

      {(label || itemCountLabel) && (
        <EdgeLabelRenderer>
          <div
            style={{
              position: "absolute",
              transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
              pointerEvents: "all",
            }}
            className="flex flex-col items-center gap-1"
          >
            {label && (
              <div className="rounded-md border border-medium-emphasis bg-background px-1.5 py-0.5 text-[10px] tracking-wider text-medium-emphasis shadow-sm dark:border-accent dark:text-primary">
                {label}
              </div>
            )}
            {itemCountLabel && (
              <div className="rounded-full bg-secondary px-2 py-0.5 text-[9px] font-semibold text-secondary-foreground shadow-sm">
                {itemCountLabel}
              </div>
            )}
          </div>
        </EdgeLabelRenderer>
      )}
    </>
  );
};
