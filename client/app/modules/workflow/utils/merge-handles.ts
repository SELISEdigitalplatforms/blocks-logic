import type { NodeHandle } from "../models/node.model";

export const MERGE_TARGET_PREFIX = "merge-handle-target-";

export const MERGE_MIN_INPUTS = 2;
export const MERGE_MAX_INPUTS = 10;

/** Number of inputs, the same rule as the server: missing or not finite 2, otherwise floored and kept within 2 to 10. */
export const mergeInputCount = (numberInputs: unknown): number => {
  if (typeof numberInputs !== "number" || !Number.isFinite(numberInputs)) return MERGE_MIN_INPUTS;
  return Math.min(MERGE_MAX_INPUTS, Math.max(MERGE_MIN_INPUTS, Math.floor(numberInputs)));
};

/** Merge's handle from its parameters: inputs merge-handle-target-1..n ("Input k") and one output. */
export const deriveMergeHandle = (parameters: Record<string, unknown> | undefined): NodeHandle => {
  const count = mergeInputCount(parameters?.numberInputs);
  return {
    targets: Array.from({ length: count }, (_, i) => ({ key: `${MERGE_TARGET_PREFIX}${i + 1}`, label: `Input ${i + 1}` })),
    sources: [{ key: "source", label: "" }],
  };
};

type MergeEdge = { source: string; target: string; sourceHandle?: string | null; targetHandle?: string | null };

/**
 * How many items each input received on Merge's latest run, grouped the way the server groups them: by the
 * edge's target handle (a blank one is Input 1), counting the source's items on that edge's branch.
 */
export const mergeInputCounts = (
  nodeId: string,
  numberInputs: unknown,
  edges: MergeEdge[],
  countsBySource: (sourceId: string) => Record<string, number> | undefined,
): { label: string; count: number }[] => {
  const counts = Array.from({ length: mergeInputCount(numberInputs) }, () => 0);
  for (const edge of edges) {
    if (edge.target !== nodeId) continue;
    const input = edge.targetHandle ? Number(edge.targetHandle.slice(MERGE_TARGET_PREFIX.length)) : 1;
    if (!edge.targetHandle?.startsWith(MERGE_TARGET_PREFIX) && edge.targetHandle) continue;
    if (!Number.isInteger(input) || input < 1 || input > counts.length) continue;
    counts[input - 1] += countsBySource(edge.source)?.[edge.sourceHandle || "source"] ?? 0;
  }
  return counts.map((count, i) => ({ label: `Input ${i + 1}`, count }));
};
