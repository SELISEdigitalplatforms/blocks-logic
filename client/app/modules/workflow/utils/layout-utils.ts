import { Edge } from "@xyflow/react";
import { EditorNode } from "@blocks-workflow/models/node.model";

const NODE_WIDTH = 200;
const NODE_HEIGHT = 150;
const HORIZONTAL_SPACING = 70;
const VERTICAL_SPACING = 50;

/**
 * Custom zero-dependency algorithm to automatically arrange workflow nodes.
 * It uses a layer-based approach where it determines the longest path
 * from root nodes to place them horizontally, and centers nodes in the same layer vertically.
 * 
 * Separated for easier customization and performance optimization later.
 */
export const getLayoutedElements = (
  nodesMap: Record<string, EditorNode>,
  edgesMap: Record<string, Edge>
): Record<string, EditorNode> => {
  const nodesArray = Object.values(nodesMap);
  const edgesArray = Object.values(edgesMap);

  if (nodesArray.length === 0) return nodesMap;

  // 1. Compute Layers using a longest-path approach (Bellman-Ford style)
  // This ensures a node is placed to the right of ALL its dependencies.
  const layerMap: Record<string, number> = {};
  nodesArray.forEach((node) => {
    layerMap[node.id] = 0;
  });

  const V = nodesArray.length;
  // Iterate at most V times to prevent infinite loops in cyclic graphs
  for (let i = 0; i < V; i++) {
    let changed = false;
    edgesArray.forEach((edge) => {
      const u = edge.source;
      const v = edge.target;
      if (layerMap[u] !== undefined && layerMap[v] !== undefined) {
        if (layerMap[u] + 1 > layerMap[v]) {
          layerMap[v] = layerMap[u] + 1;
          changed = true;
        }
      }
    });
    if (!changed) break;
  }

  // 2. Group nodes by their computed layer
  const layers: Record<number, string[]> = {};
  nodesArray.forEach((node) => {
    const l = layerMap[node.id];
    if (!layers[l]) layers[l] = [];
    layers[l].push(node.id);
  });

  // 3. Calculate new coordinates
  const newNodesMap: Record<string, EditorNode> = { ...nodesMap };

  Object.entries(layers).forEach(([layerStr, nodeIds]) => {
    const layer = parseInt(layerStr, 10);
    // Horizontal spacing based on layer depth
    const x = layer * (NODE_WIDTH + HORIZONTAL_SPACING);

    // Center nodes vertically based on how many share this layer
    const totalHeight = nodeIds.length * NODE_HEIGHT + (nodeIds.length - 1) * VERTICAL_SPACING;
    let startY = -totalHeight / 2;

    nodeIds.forEach((nodeId, index) => {
      const y = startY + index * (NODE_HEIGHT + VERTICAL_SPACING);
      newNodesMap[nodeId] = {
        ...newNodesMap[nodeId],
        position: { x, y },
      };
    });
  });

  return newNodesMap;
};
