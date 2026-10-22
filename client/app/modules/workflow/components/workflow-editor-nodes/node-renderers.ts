import { createElement } from "react";
import type { ComponentType } from "react";
import type { NodeProps } from "@xyflow/react";
import { useWorkflowStore } from "../../store";
import { NodeDefinitions } from "../node-library-panel/node-definitions";
import { EditorNodeSimple } from "./editor-node-simple";

type NodeRenderer = ComponentType<NodeProps>;

/** Renderer per node type and version. A version without an entry renders with the type's "v1" renderer. */
export const NODE_RENDERERS: Record<string, Record<string, NodeRenderer>> = Object.fromEntries(
  NodeDefinitions.map((definition) => [
    definition.type,
    {
      v1: EditorNodeSimple as unknown as NodeRenderer,
      v2: EditorNodeSimple as unknown as NodeRenderer,
    },
  ]),
);

export const resolveNodeRenderer = (type: string, version: string | undefined): NodeRenderer | undefined => {
  const byVersion = NODE_RENDERERS[type];
  if (!byVersion) return undefined;
  return (version && byVersion[version]) || byVersion.v1;
};

/** One stable xyflow node type per node type; it picks the renderer from the node's version. */
const createVersionedNode = (type: string): NodeRenderer => {
  const VersionedNode = (props: NodeProps) => {
    const version = useWorkflowStore((state) => state.nodesMap[props.id]?.version);
    const Renderer = resolveNodeRenderer(type, version);
    return Renderer ? createElement(Renderer, props) : null;
  };
  VersionedNode.displayName = `VersionedNode(${type})`;
  return VersionedNode;
};

export const VERSIONED_NODE_TYPES: Record<string, NodeRenderer> = Object.fromEntries(
  Object.keys(NODE_RENDERERS).map((type) => [type, createVersionedNode(type)]),
);
