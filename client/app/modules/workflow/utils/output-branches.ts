import type { WorkflowNode } from "../models/node.model";
import { handleDisplayLabel } from "./handle-labels";
import { findNodeDefinition } from "./node-upgraders";
import { SWITCH_FALLBACK_KEY } from "./switch-handles";

export type OutputBranchGroup = {
  /** The branch key the items carry (the handle key); shown only for debugging. */
  key: string;
  /** What a person reads: "Output 0 · Paid", "Fallback", "True". */
  title: string;
  rows: unknown[];
};

type BranchNode = Pick<WorkflowNode, "category" | "type" | "version" | "handle">;

/** "Output <i>" plus the output's name, for nodes whose outputs are a numbered list (Switch). */
const numberedTitle = (node: BranchNode, key: string, index: number) => {
  const label = node.handle?.sources?.[index]?.label;
  if (key === SWITCH_FALLBACK_KEY) return label || "Fallback";
  return label ? `Output ${index} · ${label}` : `Output ${index}`;
};

/**
 * A node's output items grouped by branch, in the node's own output order. For a node with several outputs
 * every output is listed, including those that received no items, so the panel shows where items did not
 * go. Items on a branch the node no longer has (an output deleted after the run) follow, titled by their key.
 */
export const outputBranchGroups = (node: BranchNode, rowsByBranch: Map<string, unknown[]>): OutputBranchGroup[] => {
  const sources = node.handle?.sources ?? [];
  // A single-output node keeps today's grouping (by the items' own branch), so it shows no new headers.
  if (sources.length <= 1) {
    return Array.from(rowsByBranch, ([key, rows]) => ({ key, title: handleDisplayLabel(node, "source", key) || key, rows }));
  }
  const numbered = !!findNodeDefinition(node)?.handleLabel;
  const groups: OutputBranchGroup[] = sources.map((entry, index) => ({
    key: entry.key,
    title: numbered ? numberedTitle(node, entry.key, index) : handleDisplayLabel(node, "source", entry.key) || entry.key,
    rows: rowsByBranch.get(entry.key) ?? [],
  }));
  const known = new Set(sources.map((entry) => entry.key));
  for (const [key, rows] of rowsByBranch) {
    if (!known.has(key)) groups.push({ key, title: key, rows });
  }
  return groups;
};

/** Branch headers are shown only when there is more than one group: single-output nodes look as before. */
export const showsBranchHeaders = (groups: OutputBranchGroup[]) => groups.length > 1;

export const itemCountLabel = (count: number) => (count === 0 ? "no items" : count === 1 ? "1 item" : `${count} items`);
