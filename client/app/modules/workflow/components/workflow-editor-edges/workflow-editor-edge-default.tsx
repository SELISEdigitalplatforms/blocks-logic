import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from "@xyflow/react";
import { getHandleLabel } from "@blocks-workflow/constants";

export const WorkflowEditorEdgeDefault = ({
  id,
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

  const label = getHandleLabel(sourceHandleId);

  return (
    <>
      <BaseEdge 
        id={id} 
        path={edgePath} 
        markerEnd={markerEnd} 
        style={{
          ...style,
          strokeWidth: style?.strokeWidth || 1,
          stroke: selected ? "hsl(var(--primary))" : style?.stroke,
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
