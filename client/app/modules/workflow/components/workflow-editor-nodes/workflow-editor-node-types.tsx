import { DefaultEdgeOptions, NodeProps } from "@xyflow/react";
import { EditorNodeSimple } from "./editor-node-simple";
import { NodeDefinitions } from "../node-library-panel";

export const WorkflowEditorDefaultEdgeOptions: DefaultEdgeOptions = {
  type: "default",
  markerEnd: { type: "arrow", height: 25, width: 25 },
};

export const NODE_TYPE_REGISTER: { [key: string]: React.ComponentType<NodeProps> } = {};

export const WorkflowEditorNodeTypes = (function () {
  const nodeTypes = NodeDefinitions.reduce(
    (acc, def) => {
      if (def.type in NODE_TYPE_REGISTER) {
        acc[def.type] = NODE_TYPE_REGISTER[def.type] as unknown as React.ComponentType<NodeProps>;
        return acc;
      }
      acc[def.type] = EditorNodeSimple as unknown as React.ComponentType<NodeProps>;
      return acc;
    },
    {} as Record<string, React.ComponentType<NodeProps>>,
  );
  return nodeTypes;
})();
