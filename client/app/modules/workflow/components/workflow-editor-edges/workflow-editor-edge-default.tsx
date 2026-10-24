import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from "@xyflow/react";
import { useWorkflow } from "../../hooks";
import { useWorkflowStore } from "../../store";
import { handleDisplayLabel } from "../../utils/handle-labels";
import { getStatusStyles } from "../../utils/workflow-execution-editor.util";

/** Space between a source handle and its output's name on the edge, matching the input side's `mr-1.5`. */
const SOURCE_LABEL_GAP = 6;

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

  const sourceNode = useWorkflowStore((state) => state.nodesMap[source]);
  const label = handleDisplayLabel(sourceNode, "source", sourceHandleId);

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
          {/* The output's name sits right after its handle, as a Merge input's name sits right before its own,
              so each end of the edge reads at the node it belongs to. */}
          {label && (
            <div
              style={{
                position: "absolute",
                transform: `translate(${sourceX + SOURCE_LABEL_GAP}px, ${sourceY}px) translateY(-50%)`,
                pointerEvents: "none",
              }}
              className="whitespace-nowrap rounded-md border border-medium-emphasis bg-background px-1.5 py-0.5 text-[10px] tracking-wider text-medium-emphasis shadow-sm dark:border-accent dark:text-primary"
            >
              {label}
            </div>
          )}
          {itemCountLabel && (
            <div
              style={{
                position: "absolute",
                transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
                pointerEvents: "all",
              }}
              className="rounded-full bg-secondary px-2 py-0.5 text-[9px] font-semibold text-secondary-foreground shadow-sm"
            >
              {itemCountLabel}
            </div>
          )}
        </EdgeLabelRenderer>
      )}
    </>
  );
};
