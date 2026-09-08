import { v4 as uuidv4 } from "uuid";

export const MAX_IMPORT_BYTES = 5 * 1024 * 1024;

export type ImportErrorCode =
  | "IMPORT_TOO_LARGE"
  | "IMPORT_NOT_JSON"
  | "IMPORT_BAD_SHAPE"
  | "CREATE_FAILED"
  | "UPDATE_FAILED"
  | "EXPORT_FAILED";

export const IMPORT_ERROR_MESSAGES: Record<ImportErrorCode, string> = {
  IMPORT_TOO_LARGE: "This file is larger than the 5 MB limit.",
  IMPORT_NOT_JSON: "This file is not valid JSON.",
  IMPORT_BAD_SHAPE:
    "This file is not a valid workflow export (missing name, nodes, edges or settings).",
  CREATE_FAILED: "Could not create the workflow. Please try again.",
  UPDATE_FAILED: "The workflow was created but its contents could not be saved.",
  EXPORT_FAILED: "Could not export this workflow. Please try again.",
};

export const IMPORT_SUCCESS_MESSAGE = "Workflow imported.";

export const importSuccessMessage = (issues: number): string =>
  issues > 0
    ? `Workflow imported. ${issues} item(s) were skipped because they were invalid or disconnected.`
    : IMPORT_SUCCESS_MESSAGE;

export interface WorkflowImportRoot {
  name: string;
  settings: Record<string, unknown>;
  nodes: unknown[];
  edges: unknown[];
}

export type PreflightResult =
  | { ok: true; root: WorkflowImportRoot }
  | { ok: false; code: ImportErrorCode; message: string };

const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const fail = (code: ImportErrorCode): PreflightResult => ({
  ok: false,
  code,
  message: IMPORT_ERROR_MESSAGES[code],
});

/**
 * PRE-FLIGHT VALIDATION — a hard fail here means nothing is created.
 *   V1 size <= 5 MB
 *   V2 parses as JSON and root is a non-null object
 *   V3 root.name is a non-empty (trimmed) string
 *   V4 root.nodes is an array
 *   V5 root.edges is an array
 *   V6 root.settings is a plain object
 */
export const preflightWorkflowFile = (input: { text: string; size: number }): PreflightResult => {
  if (input.size > MAX_IMPORT_BYTES) return fail("IMPORT_TOO_LARGE");

  let parsed: unknown;
  try {
    parsed = JSON.parse(input.text);
  } catch {
    return fail("IMPORT_NOT_JSON");
  }
  if (!isPlainObject(parsed)) return fail("IMPORT_NOT_JSON");

  const { name, nodes, edges, settings } = parsed as Record<string, unknown>;
  if (typeof name !== "string" || name.trim().length === 0) return fail("IMPORT_BAD_SHAPE");
  if (!Array.isArray(nodes)) return fail("IMPORT_BAD_SHAPE");
  if (!Array.isArray(edges)) return fail("IMPORT_BAD_SHAPE");
  if (!isPlainObject(settings)) return fail("IMPORT_BAD_SHAPE");

  return {
    ok: true,
    root: {
      name,
      settings,
      nodes,
      edges,
    },
  };
};

const NON_TOKEN_HEX = /[0-9A-Za-z]/;

/**
 * Replace every whole-token occurrence of each `oldId` with its `newId` inside
 * an arbitrary string. A "token" is a run of hex/alphanumeric chars, so an id
 * embedded as `node_<id>_<handle>` or standing alone as a `path` value is
 * rewritten, but a chance substring inside a longer alphanumeric run is not.
 */
export const replaceIdsInString = (
  value: string,
  idMap: ReadonlyMap<string, string>,
): string => {
  let out = value;
  for (const [oldId, newId] of idMap) {
    if (!oldId || oldId === newId || !out.includes(oldId)) continue;
    let result = "";
    let from = 0;
    let idx = out.indexOf(oldId, from);
    while (idx !== -1) {
      const before = idx > 0 ? out[idx - 1] : "";
      const afterIdx = idx + oldId.length;
      const after = afterIdx < out.length ? out[afterIdx] : "";
      const isWholeToken = !NON_TOKEN_HEX.test(before) && !NON_TOKEN_HEX.test(after);
      result += out.slice(from, idx) + (isWholeToken ? newId : oldId);
      from = afterIdx;
      idx = out.indexOf(oldId, from);
    }
    result += out.slice(from);
    out = result;
  }
  return out;
};

const remapValueIds = <T>(value: T, idMap: ReadonlyMap<string, string>): T => {
  if (value === undefined) return value;
  const json = JSON.stringify(value);
  if (json === undefined) return value;
  return JSON.parse(replaceIdsInString(json, idMap)) as T;
};

export interface RemappedWorkflow {
  nodes: Array<Record<string, unknown>>;
  edges: Array<Record<string, unknown>>;
  settings: Record<string, unknown>;
  issues: number;
}

const hasNumericPosition = (value: unknown): boolean =>
  isPlainObject(value) &&
  typeof value.x === "number" &&
  Number.isFinite(value.x) &&
  typeof value.y === "number" &&
  Number.isFinite(value.y);

const isNonEmptyString = (value: unknown): value is string =>
  typeof value === "string" && value.length > 0;

const isValidImportNode = (node: unknown): node is Record<string, unknown> =>
  isPlainObject(node) &&
  isNonEmptyString(node.id) &&
  isNonEmptyString(node.name) &&
  isNonEmptyString(node.type) &&
  isNonEmptyString(node.category) &&
  isNonEmptyString(node.version) &&
  hasNumericPosition(node.position);

const freshNodeId = (): string => uuidv4().replace(/-/g, "");

/**
 * REMAP + SANITISE. Post-create issues are NON-fatal: malformed nodes,
 * duplicate node ids and dangling edges are dropped and counted, but the
 * workflow is still saved.
 */
export const remapAndSanitiseWorkflow = (root: WorkflowImportRoot): RemappedWorkflow => {
  let issues = 0;

  // a. drop nodes that are not objects or are missing required fields.
  const valid: Array<Record<string, unknown>> = [];
  for (const node of root.nodes) {
    if (isValidImportNode(node)) {
      valid.push(node);
    } else {
      issues += 1;
    }
  }

  // b. de-duplicate by id, keeping the first.
  const seen = new Set<string>();
  const survivors: Array<Record<string, unknown>> = [];
  for (const node of valid) {
    const id = node.id as string;
    if (seen.has(id)) {
      issues += 1;
      continue;
    }
    seen.add(id);
    survivors.push(node);
  }

  // c. build the full oldId -> newId map before rewriting anything.
  const idMap = new Map<string, string>();
  for (const node of survivors) {
    idMap.set(node.id as string, freshNodeId());
  }

  // d. rewrite each surviving node.
  const nodes = survivors.map((node) => {
    const oldId = node.id as string;
    const newId = idMap.get(oldId) as string;
    const next: Record<string, unknown> = { ...node, id: newId };
    if ("parameters" in node) next.parameters = remapValueIds(node.parameters, idMap);
    if ("settings" in node) next.settings = remapValueIds(node.settings, idMap);
    if ("pinData" in node) next.pinData = remapValueIds(node.pinData, idMap);
    return next;
  });

  // e. rewrite whole-token ids inside workflow-level settings string values.
  const settings: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(root.settings)) {
    settings[key] = typeof value === "string" ? replaceIdsInString(value, idMap) : value;
  }

  // f. rewrite / drop edges.
  const edges: Array<Record<string, unknown>> = [];
  const usedEdgeIds = new Set<string>();
  root.edges.forEach((edge, i) => {
    if (!isPlainObject(edge)) {
      issues += 1;
      return;
    }
    const newSource = typeof edge.source === "string" ? idMap.get(edge.source) : undefined;
    const newTarget = typeof edge.target === "string" ? idMap.get(edge.target) : undefined;
    if (!newSource || !newTarget) {
      issues += 1;
      return;
    }
    let id = `xy-edge__${newSource}-${newTarget}`;
    if (usedEdgeIds.has(id)) id = `${id}-${i}`;
    usedEdgeIds.add(id);
    edges.push({ ...edge, id, source: newSource, target: newTarget });
  });

  return { nodes, edges, settings, issues };
};
