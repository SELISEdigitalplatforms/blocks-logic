import { Workflow } from "../models/workflow.model";

/**
 * Shape of a workflow export file. It is EXACTLY the workflow definition — no
 * wrapper, no `schemaVersion` — carrying only the fields needed to rebuild the
 * graph. Version / publish / audit / execution data is never written.
 */
export interface WorkflowExportNode {
  id: string;
  name: string;
  category: string;
  type: string;
  version: string;
  position: { x: number; y: number };
  handle?: unknown;
  parameters: Record<string, unknown>;
  settings: Record<string, unknown>;
  pinData: unknown[] | null;
}

export interface WorkflowExportEdge {
  id: string;
  source: string;
  target: string;
  sourceHandle: string;
  targetHandle: string;
  [k: string]: unknown;
}

export interface WorkflowExportFile {
  name: string;
  settings: Record<string, string>;
  nodes: WorkflowExportNode[];
  edges: WorkflowExportEdge[];
}

const isPlainObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === "object" && value !== null && !Array.isArray(value);

const pickNode = (node: Record<string, unknown>): WorkflowExportNode => {
  const position = isPlainObject(node.position)
    ? { x: Number(node.position.x), y: Number(node.position.y) }
    : { x: 0, y: 0 };

  const exported: WorkflowExportNode = {
    id: String(node.id ?? ""),
    name: String(node.name ?? ""),
    category: String(node.category ?? ""),
    type: String(node.type ?? ""),
    version: String(node.version ?? ""),
    position,
    parameters: isPlainObject(node.parameters) ? node.parameters : {},
    settings: isPlainObject(node.settings) ? node.settings : {},
    // KEPT verbatim (remapped on import). `undefined` is normalised to `null`.
    pinData: Array.isArray(node.pinData) ? node.pinData : null,
  };

  if (node.handle !== undefined) {
    exported.handle = node.handle;
  }

  return exported;
};

const pickEdge = (edge: Record<string, unknown>): WorkflowExportEdge => ({
  // Spread first so any extra xyflow props are preserved, then pin the
  // structural fields to strings.
  ...edge,
  id: String(edge.id ?? ""),
  source: String(edge.source ?? ""),
  target: String(edge.target ?? ""),
  sourceHandle: String(edge.sourceHandle ?? ""),
  targetHandle: String(edge.targetHandle ?? ""),
});

/**
 * Build the export file payload from a `Workflow/Get` response entity. Only
 * `name`, `settings`, `nodes` and `edges` survive; every
 * itemId / tenantId / publish / audit / execution field is dropped.
 */
export const buildWorkflowExport = (workflow: Workflow): WorkflowExportFile => {
  const nodes = Array.isArray(workflow.nodes) ? workflow.nodes : [];
  const edges = Array.isArray(workflow.edges) ? workflow.edges : [];

  return {
    name: typeof workflow.name === "string" ? workflow.name : "",
    settings: isPlainObject(workflow.settings)
      ? (workflow.settings as Record<string, string>)
      : {},
    nodes: nodes.map((node) => pickNode(node as unknown as Record<string, unknown>)),
    edges: edges.map((edge) => pickEdge(edge as unknown as Record<string, unknown>)),
  };
};

/**
 * `name` → url-safe slug: lowercase, non-alphanumeric runs collapse to `-`,
 * leading / trailing `-` trimmed.
 */
export const slugifyWorkflowName = (name: string): string =>
  (name || "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "");

const pad2 = (n: number): string => String(n).padStart(2, "0");

/**
 * `<slug(name)|"workflow">-<YYYY-MM-DD>.json`
 */
export const workflowExportFileName = (name: string, date: Date = new Date()): string => {
  const slug = slugifyWorkflowName(name) || "workflow";
  const stamp = `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
  return `${slug}-${stamp}.json`;
};
