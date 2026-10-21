import type { NodeHandle } from "../models/node.model";

const keysOf = (entries: NodeHandle["sources"] | undefined): string[] =>
  Array.isArray(entries) ? entries.map((entry) => entry.key) : [];

/** Handle keys present in `before` but gone from `after`, per side. */
export const vanishedHandleKeys = (
  before: NodeHandle | null | undefined,
  after: NodeHandle | null | undefined,
): { sources: string[]; targets: string[] } => {
  const afterSources = new Set(keysOf(after?.sources));
  const afterTargets = new Set(keysOf(after?.targets));
  return {
    sources: keysOf(before?.sources).filter((key) => !afterSources.has(key)),
    targets: keysOf(before?.targets).filter((key) => !afterTargets.has(key)),
  };
};
