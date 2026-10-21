import type { WorkflowNode } from "../models/node.model";

/** The node fields the server stores (NodeDto). Editor-only fields (data, selected, measured …) are not sent. */
export type PersistedNode = Pick<
  WorkflowNode,
  "id" | "name" | "category" | "type" | "version" | "position" | "handle" | "parameters" | "settings" | "pinData"
>;

export const toPersistedNode = (node: WorkflowNode): PersistedNode => ({
  id: node.id,
  name: node.name,
  category: node.category,
  type: node.type,
  version: node.version,
  position: node.position,
  handle: node.handle,
  parameters: node.parameters,
  settings: node.settings,
  pinData: node.pinData,
});
