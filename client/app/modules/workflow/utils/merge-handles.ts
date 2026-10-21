import type { NodeHandle } from "../models/node.model";

export const MERGE_TARGET_PREFIX = "merge-handle-target-";

/** Number of inputs, the same rule as the server: missing or not finite 2, below 1 1, otherwise floored. */
export const mergeInputCount = (numberInputs: unknown): number => {
  if (typeof numberInputs !== "number" || !Number.isFinite(numberInputs)) return 2;
  return Math.max(1, Math.floor(numberInputs));
};

/** Merge's handle from its parameters: inputs merge-handle-target-1..n ("Input k") and one output. */
export const deriveMergeHandle = (parameters: Record<string, unknown> | undefined): NodeHandle => {
  const count = mergeInputCount(parameters?.numberInputs);
  return {
    targets: Array.from({ length: count }, (_, i) => ({ key: `${MERGE_TARGET_PREFIX}${i + 1}`, label: `Input ${i + 1}` })),
    sources: [{ key: "source", label: "" }],
  };
};
