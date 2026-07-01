import { Edge } from "@xyflow/react";
import { ExecutedItem } from "../models/workflow.model";
import { EditorNode } from "@blocks-workflow/models/node.model";

/**
 * Finds all predecessor nodes of a given node using a DFS approach.
 * Uses execution data if available (parentItemIds), otherwise falls back to static graph (edges).
 */
export const getAllPredecessors = (
  nodeId: string,
  nodesMap: Record<string, EditorNode>,
  edgesMap: Record<string, Edge>,
  executedItems: ExecutedItem[] = []
): EditorNode[] => {
  const predecessorNodeIds = new Set<string>();

  if (executedItems.length > 0) {
    // 1. Using Execution Data
    const itemMap = new Map(executedItems.map((item) => [item.itemId, item]));
    const stack: string[] = [];

    // Find initial items for the target node
    executedItems.forEach((item) => {
      if (item.nodeId === nodeId) {
        if (item.parentItemIds) {
          stack.push(...item.parentItemIds);
        }
      }
    });

    const visitedItems = new Set<string>();

    while (stack.length > 0) {
      const currentItemId = stack.pop()!;
      if (visitedItems.has(currentItemId)) continue;
      visitedItems.add(currentItemId);

      const item = itemMap.get(currentItemId);
      if (item) {
        predecessorNodeIds.add(item.nodeId);
        if (item.parentItemIds) {
          stack.push(...item.parentItemIds);
        }
      }
    }
  } else {
    // 2. Using Static Graph (edgesMap)
    const stack: string[] = [nodeId];
    const visitedNodes = new Set<string>();

    while (stack.length > 0) {
      const currentNodeId = stack.pop()!;
      if (visitedNodes.has(currentNodeId)) continue;
      visitedNodes.add(currentNodeId);

      // Find all incoming edges to currentNodeId
      const incomingEdges = Object.values(edgesMap).filter(
        (e) => e.target === currentNodeId
      );
      for (const edge of incomingEdges) {
        const sourceNodeId = edge.source;
        predecessorNodeIds.add(sourceNodeId);
        stack.push(sourceNodeId);
      }
    }
  }

  // Remove the initial node if it accidentally got added (e.g., due to cycles)
  predecessorNodeIds.delete(nodeId);

  return Array.from(predecessorNodeIds)
    .map((id) => nodesMap[id])
    .filter(Boolean);
};
