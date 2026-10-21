import type { ExecutedItem, ExecutedNode, WorkflowEdge } from "../models/workflow.model";
import type { WorkflowNode } from "../models/node.model";

export enum NodeExecutionStatus {
  Pending = 2,
  Running = 3,
  Completed = 4,
  Failed = 5,
}

export const getStatusStyles = (status: number) => {
  switch (status) {
    case NodeExecutionStatus.Pending:
      return {
        nodeClass: "border-yellow-500",
        edgeColor: "#eab308",
        edgeClass: "stroke-yellow-500",
      };
    case NodeExecutionStatus.Running:
      return {
        nodeClass: "border-purple-500",
        edgeColor: "#a855f7",
        edgeClass: "stroke-purple-500",
      };
    case NodeExecutionStatus.Completed:
      return {
        nodeClass: "border-success",
        edgeColor: "#18c964",
        edgeClass: "stroke-success",
      };
    case NodeExecutionStatus.Failed:
      return {
        nodeClass: "border-destructive",
        edgeColor: "#ef4444",
        edgeClass: "stroke-destructive",
      };
    default:
      return {
        nodeClass: "",
        edgeColor: "#94a3b8",
        edgeClass: "",
      };
  }
};

/**
 * Maps edge sourceHandle IDs to the branch keys used in
 * ExecutedNode.outputCountsByBranch.
 */
const HANDLE_TO_BRANCH_KEY: Record<string, string> = {
  "if-true": "True",
  "if-false": "False",
};

/**
 * An edge is branching when its source node has more than one output, or when it leaves through a
 * handle other than the default "source" (a single-output node with its own key, e.g. a one-rule Switch).
 * Every other edge is always traversed.
 */
const isBranchingEdge = (edge: WorkflowEdge, sourceNode: WorkflowNode | undefined): boolean => {
  const handle = edge.sourceHandle;
  if ((sourceNode?.handle?.sources?.length ?? 0) > 1) return true;
  return !!handle && handle !== "source";
};

/**
 * Determines if an edge was actually traversed during execution.
 *
 * Strategy (tried in order):
 *   1. Check `outputCountsByBranch` with the mapped branch key (e.g. "True")
 *   2. Check `outputCountsByBranch` with the raw handle id (e.g. "if-true")
 *   3. Fallback: check whether the edge's **target** node actually executed
 *      (i.e. it appears in the set of nodes that processed items).
 */
const wasEdgeTraversed = (
  edge: WorkflowEdge,
  sourceNode: WorkflowNode | undefined,
  executionNode: ExecutedNode,
  actuallyExecutedNodeIds: Set<string>,
): boolean => {
  const handle = edge.sourceHandle;

  // Non-branching edge — always considered traversed
  if (!isBranchingEdge(edge, sourceNode)) {
    return true;
  }

  // --- Strategy 1 & 2: use outputCountsByBranch if available ---------------
  const branchCounts = executionNode.outputCountsByBranch;
  if (branchCounts && Object.keys(branchCounts).length > 0) {
    // Try mapped key first ("if-true" → "True")
    const mappedKey = handle ? HANDLE_TO_BRANCH_KEY[handle] : undefined;
    if (mappedKey && mappedKey in branchCounts) {
      return branchCounts[mappedKey] > 0;
    }
    // Try raw handle id ("if-true")
    if (handle && handle in branchCounts) {
      return branchCounts[handle] > 0;
    }
  }

  // --- Strategy 3: fallback — did the downstream node actually run? --------
  return actuallyExecutedNodeIds.has(edge.target);
};

/**
 * Builds the sets of node-IDs and edge-IDs that belong to the
 * *actually-executed* path through the workflow graph.
 *
 * The algorithm:
 *  1. Determines which nodes actually executed by checking `data.items` and
 *     `inputItemCount / outputItemCount` from node-execution records.
 *  2. Trigger nodes (no incoming edges) that appear in nodeExecutions are
 *     always included.
 *  3. A BFS from the trigger(s) follows only traversed edges.
 */
export const buildExecutedSubgraph = (
  nodes: WorkflowNode[],
  edges: WorkflowEdge[],
  nodeExecutions: ExecutedNode[],
  items: ExecutedItem[],
): { reachableNodeIds: Set<string>; traversedEdgeIds: Set<string> } => {
  const nodesById = new Map(nodes.map((node) => [node.id, node]));

  // Map nodeId → ExecutedNode for fast lookup
  const nodeExecutionMap = new Map(
    nodeExecutions.map((ne) => [ne.nodeId, ne]),
  );

  // Nodes that genuinely processed data (appear in items or have item counts)
  const itemNodeIds = new Set(items.map((item) => item.nodeId));

  const actuallyExecutedNodeIds = new Set<string>();
  for (const ne of nodeExecutions) {
    if (
      itemNodeIds.has(ne.nodeId) ||
      ne.inputItemCount > 0 ||
      ne.outputItemCount > 0
    ) {
      actuallyExecutedNodeIds.add(ne.nodeId);
    }
  }

  // Trigger / start nodes have no incoming edges — always mark them executed
  // if they appear in nodeExecutions.
  const targetNodeIds = new Set(edges.map((e) => e.target));
  for (const node of nodes) {
    if (!targetNodeIds.has(node.id) && nodeExecutionMap.has(node.id)) {
      actuallyExecutedNodeIds.add(node.id);
    }
  }

  // Group edges by source for efficient lookup
  const edgesBySource = new Map<string, WorkflowEdge[]>();
  for (const edge of edges) {
    const list = edgesBySource.get(edge.source) || [];
    list.push(edge);
    edgesBySource.set(edge.source, list);
  }

  // BFS from trigger nodes through traversed edges
  const reachableNodeIds = new Set<string>();
  const traversedEdgeIds = new Set<string>();
  const queue: string[] = [];

  for (const node of nodes) {
    if (!targetNodeIds.has(node.id) && actuallyExecutedNodeIds.has(node.id)) {
      queue.push(node.id);
    }
  }

  while (queue.length > 0) {
    const nodeId = queue.shift()!;
    if (reachableNodeIds.has(nodeId)) continue;
    reachableNodeIds.add(nodeId);

    const execNode = nodeExecutionMap.get(nodeId);
    if (!execNode) continue;

    // A failed node was reached but its downstream nodes were not —
    // stop the BFS here so nodes after the failure stay uncolored.
    if (execNode.status === NodeExecutionStatus.Failed) continue;

    const outEdges = edgesBySource.get(nodeId) || [];
    for (const edge of outEdges) {
      if (wasEdgeTraversed(edge, nodesById.get(edge.source), execNode, actuallyExecutedNodeIds)) {
        traversedEdgeIds.add(edge.id);
        // Visit any node that has an execution record (including Failed ones)
        // so they get colored with the appropriate status style.
        if (nodeExecutionMap.has(edge.target)) {
          queue.push(edge.target);
        }
      }
    }
  }

  return { reachableNodeIds, traversedEdgeIds };
};
