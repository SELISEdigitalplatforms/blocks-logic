// Import files for the Filter node spec (tests/03-workflow/filter-node.spec.ts).
//
// Shape = the editor's export / import file (client utils/workflow-export.ts and
// utils/workflow-import.ts; server Import/WorkflowImportMapper.cs): { name, settings, nodes, edges }.
// Node and edge ids are rewritten on import (whole-token replace, also inside parameters), so the
// ids below only need to be unique alphanumeric tokens. The spec finds nodes by their names.

export type ImportNode = {
  id: string
  name: string
  category: string
  type: string
  version: string
  position: { x: number; y: number }
  handle: { sources: { key: string; label: string }[]; targets: { key: string; label: string }[] }
  parameters: Record<string, unknown>
  settings: Record<string, unknown>
  pinData: null
}

export type ImportEdge = {
  id: string
  source: string
  target: string
  sourceHandle: string
  targetHandle: string
}

export type WorkflowImportFile = {
  name: string
  settings: Record<string, string>
  nodes: ImportNode[]
  edges: ImportEdge[]
}

export type FilterCondition = { left: string; operator: string; right?: string; type?: string }

/** Spec §7 input, used by E1–E4. The webhook turns an array body into one item per element. */
export const E1_ITEMS = [
  { id: 1, status: "paid", total: 120 },
  { id: 2, status: "draft", total: 300 },
  { id: 3, status: "paid", total: 40 },
]

/** E1: AND, status equals `paid` (string), total greater_than `100` (number). */
export const E1_CONDITIONS: FilterCondition[] = [
  { left: "{{ $json.status }}", operator: "equals", right: "paid", type: "string" },
  { left: "{{ $json.total }}", operator: "greater_than", right: "100", type: "number" },
]

/**
 * H9: an expression in "After" that reads the node before Filter.
 * DECIDED: spec §10 writes `{{ $node["Set Field"].json.id }}`, but the server only resolves
 * `$node["<name>"].json.output.<path>` (NodeExecutorBase ResolveNodeReference / SelectNodeReference),
 * and the editor's own variable picker inserts `.json.output.<path>`. So the spec uses `.json.output.id`.
 */
export const H9_EXPRESSION = '{{ $node["Set Field"].json.output.id }}'

/** Names the spec looks nodes up by. "Set Field" must stay exactly this for H9_EXPRESSION. */
export const NODE_NAMES = {
  webhook: "Webhook",
  setField: "Set Field",
  filter: "Filter",
  after: "After",
  merge: "Merge",
} as const

const singleHandle = () => ({
  sources: [{ key: "source", label: "" }],
  targets: [{ key: "target", label: "" }],
})

const webhookNode = (id: string, x: number, y: number): ImportNode => ({
  id,
  name: NODE_NAMES.webhook,
  category: "trigger",
  type: "webhook",
  version: "v2",
  position: { x, y },
  handle: { sources: [{ key: "source", label: "" }], targets: [] },
  // node-schema-trigger-webhook-v1.ts defaults + transform (path = node id, authorizationMode).
  // "immediate" keeps the normal queued run path; authType "none" lets the test POST go anonymous.
  parameters: {
    httpMethod: "POST",
    authType: "none",
    httpResponseMode: "immediate",
    httpResponseData: "all",
    organizationId: "",
    roles: { operator: "all", values: [] },
    permissions: { operator: "any", values: [] },
    authorizationMode: "RolesOnly",
    path: id,
  },
  settings: {},
  pinData: null,
})

/**
 * "Set Field" passes every webhook item through unchanged: manual mapping with no fields plus
 * "Include Other Input Fields: All" (TransformSetFieldV1Node GetBaseItem). So it outputs the E1 items.
 */
const passThroughSetFieldNode = (id: string, x: number, y: number): ImportNode => ({
  id,
  name: NODE_NAMES.setField,
  category: "transform",
  type: "setfield",
  version: "v2",
  position: { x, y },
  handle: singleHandle(),
  parameters: {
    mode: "manual_mapping",
    manualMappingFields: [],
    includeOtherFields: true,
    otherFieldsMode: "all",
    includedFields: "",
    excludeFields: "",
  },
  settings: {},
  pinData: null,
})

/** "After": `{ seen: "{{ $json.id }}", origin: H9_EXPRESSION }` (both as strings). */
const afterNode = (id: string, x: number, y: number): ImportNode => ({
  id,
  name: NODE_NAMES.after,
  category: "transform",
  type: "setfield",
  version: "v2",
  position: { x, y },
  handle: singleHandle(),
  parameters: {
    mode: "manual_mapping",
    manualMappingFields: [
      { key: "seen", type: "string", value: "{{ $json.id }}" },
      { key: "origin", type: "string", value: H9_EXPRESSION },
    ],
    includeOtherFields: false,
    otherFieldsMode: "all",
    includedFields: "",
    excludeFields: "",
  },
  settings: {},
  pinData: null,
})

const filterNode = (
  id: string,
  x: number,
  y: number,
  conditions: FilterCondition[],
  conditionType = "and",
): ImportNode => ({
  id,
  name: NODE_NAMES.filter,
  category: "logic",
  type: "filter",
  version: "v2",
  position: { x, y },
  handle: singleHandle(),
  parameters: { conditionType, conditions },
  settings: {},
  pinData: null,
})

const mergeNode = (id: string, x: number, y: number): ImportNode => ({
  id,
  name: NODE_NAMES.merge,
  category: "transform",
  type: "merge",
  version: "v2",
  position: { x, y },
  handle: {
    sources: [{ key: "source", label: "" }],
    targets: [
      { key: "merge-handle-target-1", label: "Input 1" },
      { key: "merge-handle-target-2", label: "Input 2" },
    ],
  },
  parameters: { mode: "append", numberInputs: 2, runWhen: "any" },
  settings: {},
  pinData: null,
})

const edge = (source: string, target: string, targetHandle = "target"): ImportEdge => ({
  id: `xy-edge__${source}-${target}`,
  source,
  target,
  sourceHandle: "source",
  targetHandle,
})

const IDS = {
  webhook: "e2efilterwebhook000000000000001",
  setField: "e2efiltersetfield00000000000001",
  filter: "e2efilterfilter0000000000000001",
  after: "e2efilterafter00000000000000001",
  merge: "e2efiltermerge00000000000000001",
}

/**
 * §10 fixture, steps 1–10: Webhook → Set Field, and "After" not connected yet. The spec adds the
 * Filter from the library after Set Field (auto-connected) and then connects Filter → After.
 * After sits at x=840 so the library's preferred slot (Set Field + 260 = 520) is free for Filter.
 */
export function mainWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(IDS.webhook, 0, 0),
      passThroughSetFieldNode(IDS.setField, 260, 0),
      afterNode(IDS.after, 840, 0),
    ],
    edges: [edge(IDS.webhook, IDS.setField)],
  }
}

/** §10 step 6 (E4): Filter with the unknown operator `starts_with`. */
export function startsWithWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(IDS.webhook, 0, 0),
      passThroughSetFieldNode(IDS.setField, 260, 0),
      filterNode(IDS.filter, 520, 0, [
        { left: "{{ $json.status }}", operator: "starts_with", right: "p", type: "string" },
      ]),
      afterNode(IDS.after, 780, 0),
    ],
    edges: [
      edge(IDS.webhook, IDS.setField),
      edge(IDS.setField, IDS.filter),
      edge(IDS.filter, IDS.after),
    ],
  }
}

/**
 * §10 step 11 (C2): Filter removes every item and feeds Merge input 1; Set Field feeds Merge
 * input 2 directly. Merge "Run When" = Any.
 */
export function joinWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(IDS.webhook, 0, 0),
      passThroughSetFieldNode(IDS.setField, 260, 0),
      filterNode(IDS.filter, 520, -120, [
        { left: "{{ $json.status }}", operator: "equals", right: "none", type: "string" },
      ]),
      mergeNode(IDS.merge, 800, 0),
    ],
    edges: [
      edge(IDS.webhook, IDS.setField),
      edge(IDS.setField, IDS.filter),
      edge(IDS.filter, IDS.merge, "merge-handle-target-1"),
      edge(IDS.setField, IDS.merge, "merge-handle-target-2"),
    ],
  }
}
