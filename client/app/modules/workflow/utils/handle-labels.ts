import type { WorkflowNode } from "../models/node.model";
import { findNodeDefinition } from "./node-upgraders";

/**
 * The label a handle (and an edge leaving it) shows. A definition may compute source labels
 * (Switch: "Output <i>" for an unnamed output); every other handle shows its entry's label.
 */
export const handleDisplayLabel = (
  node: Pick<WorkflowNode, "category" | "type" | "version" | "handle"> | undefined,
  side: "source" | "target",
  key: string | null | undefined,
): string => {
  if (!node || !key) return "";
  const handleLabel = side === "source" ? findNodeDefinition(node)?.handleLabel : undefined;
  if (handleLabel) return handleLabel(node.handle, key);
  const entries = side === "source" ? node.handle?.sources : node.handle?.targets;
  return entries?.find((entry) => entry.key === key)?.label ?? "";
};
