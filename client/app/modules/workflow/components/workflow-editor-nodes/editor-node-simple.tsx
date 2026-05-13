import { EditorNodeBase } from "./editor-node-base";
import { Position, Node } from "@xyflow/react";
import { EditorNodeTitle } from "./editor-node-title";
import { useWorkflow } from "../../hooks";
import { EditorNodeHandle, EditorNodeHandleArrow } from "./editor-node-handle";
import { getNodeDefinition } from "../node-library-panel";
import { useMemo } from "react";

export const EditorNodeSimple = ({ id }: Node) => {
  const { getNodeById } = useWorkflow();
  const node = getNodeById(id);

  const nodeDefinition = useMemo(() => {
    if (!node) return null;
    return getNodeDefinition(node.category, node.type, node.version);
  }, [node]);

  if (!node) return null;
  if (!nodeDefinition) return null;

  return (
    <EditorNodeBase id={id}>
      <div className="absolute -left-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.target.map((handleId) => (
          <EditorNodeHandle
            key={handleId}
            type="target"
            position={Position.Left}
            id={handleId}
            nodeId={node.id}
          ></EditorNodeHandle>
        ))}
      </div>

      <EditorNodeTitle icon={nodeDefinition.icon} title={nodeDefinition.title} />

      <div className="absolute -right-1 top-0 flex h-full flex-col items-center justify-center gap-4">
        {nodeDefinition.handleSpec.source.map((handleId) => (
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
        ))}
      </div>
    </EditorNodeBase>
  );
};
