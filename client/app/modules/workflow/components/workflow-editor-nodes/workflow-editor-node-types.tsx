import { DefaultEdgeOptions } from "@xyflow/react";
import { VERSIONED_NODE_TYPES } from "./node-renderers";

export const WorkflowEditorDefaultEdgeOptions: DefaultEdgeOptions = {
  type: "default",
  markerEnd: { type: "arrow", height: 25, width: 25 },
};

/** xyflow node types, keyed by node type. Referentially stable: built once at module load. */
export const WorkflowEditorNodeTypes = VERSIONED_NODE_TYPES;
