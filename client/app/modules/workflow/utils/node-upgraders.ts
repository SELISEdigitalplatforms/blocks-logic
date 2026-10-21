import type {
  WorkflowNodeDefinition,
  NodeHandle,
  NodeVersion,
  WorkflowNode,
} from "../models/node.model";
import { getNodeDefinition } from "../components/node-library-panel/node-definitions";

/** Versions a stored node may carry; anything else is read as "v1". */
const KNOWN_VERSIONS: readonly string[] = ["v1", "v2", "v3"] satisfies NodeVersion[];

const cloneEntries = (entries: NodeHandle["sources"] | undefined) =>
  Array.isArray(entries) ? entries.map((entry) => ({ ...entry })) : [];

/** Deep copy; a list that is missing in stored data comes back empty. */
export const cloneHandle = (handle: NodeHandle): NodeHandle => ({
  sources: cloneEntries(handle.sources),
  targets: cloneEntries(handle.targets),
});

const isEmptyHandle = (handle: NodeHandle | null | undefined): boolean =>
  !handle ||
  ((!Array.isArray(handle.sources) || handle.sources.length === 0) &&
    (!Array.isArray(handle.targets) || handle.targets.length === 0));

/**
 * Upgraders keyed by the version they upgrade FROM. Each returns the node one version higher.
 * v1 → v2: the node takes its handle from the definition template; keys are today's handle ids, so
 * saved edges keep drawing.
 */
export const NODE_UPGRADERS: Record<
  string,
  <T extends WorkflowNode>(node: T, definition: WorkflowNodeDefinition) => T
> = {
  v1: (node, definition) => ({ ...node, version: "v2", handle: cloneHandle(definition.handle) }),
};

const normaliseVersion = (version: unknown): string =>
  typeof version === "string" && KNOWN_VERSIONS.includes(version) ? version : "v1";

/** The definition serving this node, or undefined (unknown type, or a version this bundle does not know). */
export const findNodeDefinition = (
  node: Pick<WorkflowNode, "category" | "type" | "version">,
): WorkflowNodeDefinition | undefined =>
  getNodeDefinition(node.category, node.type, normaliseVersion(node.version));

/**
 * Brings a stored node up to the newest version this bundle knows, in memory only. Pure: the input is
 * never mutated, and a node already current comes back unchanged (the same object).
 * - A missing or unknown version is read as "v1".
 * - A node without a matching definition is returned as is.
 * - Afterwards a definition with `deriveHandle` always rebuilds the handle from the parameters; any
 *   other definition fills a null, missing or empty handle from its template.
 */
export const upgradeNode = <T extends WorkflowNode>(node: T): T => {
  let version = normaliseVersion(node.version);
  let definition = getNodeDefinition(node.category, node.type, version);
  if (!definition) return node;

  let upgraded: T = version === node.version ? node : { ...node, version: version as NodeVersion };
  const seen = new Set<string>();
  while (NODE_UPGRADERS[version] && !seen.has(version)) {
    seen.add(version);
    const next = NODE_UPGRADERS[version](upgraded, definition);
    const nextDefinition = getNodeDefinition(next.category, next.type, next.version);
    if (!nextDefinition) break;
    upgraded = next;
    version = next.version;
    definition = nextDefinition;
  }

  if (definition.deriveHandle) {
    return { ...upgraded, handle: definition.deriveHandle(upgraded.parameters) };
  }
  if (isEmptyHandle(upgraded.handle)) {
    return { ...upgraded, handle: cloneHandle(definition.handle) };
  }
  return upgraded;
};
