// Import files with Code nodes (transform / code / v1) for the time-zone spec (blocks-logic#436) and
// the Monaco loader spec (blocks-logic#435). Same file shape as support/filter-fixtures.ts.

import type { ImportEdge, ImportNode, WorkflowImportFile } from "./filter-fixtures";

export const CODE_NODE_NAMES = {
  webhook: "Webhook",
  zones: "Time Zones",
  codeA: "Code A",
  codeB: "Code B",
} as const;

const singleHandle = () => ({
  sources: [{ key: "source", label: "" }],
  targets: [{ key: "target", label: "" }],
});

/**
 * Webhook trigger whose test call answers with the last node's output ("last" runs the workflow in the
 * Api that answers the call and returns `data`). authType "none" lets the test POST go anonymous.
 */
const webhookNode = (id: string, x: number, y: number): ImportNode => ({
  id,
  name: CODE_NODE_NAMES.webhook,
  category: "trigger",
  type: "webhook",
  version: "v2",
  position: { x, y },
  handle: { sources: [{ key: "source", label: "" }], targets: [] },
  parameters: {
    httpMethod: "POST",
    authType: "none",
    httpResponseMode: "last",
    httpResponseData: "all",
    organizationId: "",
    roles: { operator: "all", values: [] },
    permissions: { operator: "any", values: [] },
    authorizationMode: "RolesOnly",
    path: id,
  },
  settings: {},
  pinData: null,
});

const codeNode = (id: string, name: string, x: number, y: number, script: string): ImportNode => ({
  id,
  name,
  category: "transform",
  type: "code",
  version: "v1",
  position: { x, y },
  handle: singleHandle(),
  parameters: { mode: "all", language: "js", script },
  settings: {},
  pinData: null,
});

const edge = (source: string, target: string): ImportEdge => ({
  id: `xy-edge__${source}-${target}`,
  source,
  target,
  sourceHandle: "source",
  targetHandle: "target",
});

/**
 * blocks-logic#436 §10 steps 1–2 in one all-items Code node:
 * - step 1 (H11, the LTG #11985 reproduction): the current time in Asia/Dhaka and Europe/Zurich, the
 *   resolved zone and the error for an unknown zone. `iso` is the instant the script used, so the spec can
 *   compute the expected local times.
 * - step 2 (H1–H6): the §8 expressions as fields, each with its fixed instant.
 * Every call names its zone; nothing depends on the host zone (C4).
 */
export const TIME_ZONE_SCRIPT = `const at = (y, mo, d, h, mi, s) => new Date(Date.UTC(y, mo, d, h, mi, s || 0));
const now = new Date();
const hm = (tz) => new Intl.DateTimeFormat('en-GB', { timeZone: tz, hour: '2-digit', minute: '2-digit', hourCycle: 'h23' }).format(now);
let bad;
try {
  now.toLocaleString('en-GB', { timeZone: 'Not/AZone' });
  bad = 'no error';
} catch (e) {
  bad = String((e && e.message) || e);
}
return [{
  iso: now.toISOString(),
  dhaka: hm('Asia/Dhaka'),
  zurich: hm('Europe/Zurich'),
  resolved: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Dhaka' }).resolvedOptions().timeZone,
  bad,
  h1: at(2026, 9, 28, 11, 54, 1).toLocaleString('en-GB', { timeZone: 'Asia/Dhaka' }),
  h2: at(2026, 6, 1, 12, 0).toLocaleString('en-GB', { timeZone: 'Europe/Zurich' }),
  h3: at(2026, 0, 15, 12, 0).toLocaleString('en-GB', { timeZone: 'Europe/Zurich' }),
  h4: new Intl.DateTimeFormat('en-GB', { timeZone: 'Asia/Kathmandu', hour: '2-digit', minute: '2-digit' }).format(at(2026, 9, 28, 0, 0)),
  h5: at(2026, 9, 28, 19, 0).toLocaleDateString('en-GB', { timeZone: 'Asia/Dhaka' }),
  h6: at(2026, 9, 28, 0, 0).toLocaleTimeString('en-GB', { timeZone: 'Asia/Thimphu' }),
}];
`;

const ZONE_IDS = {
  webhook: "e2etzwebhook0000000000000000001",
  zones: "e2etzcode000000000000000000001",
};

/** Webhook → "Time Zones" (all-items Code node running TIME_ZONE_SCRIPT). */
export function timeZoneWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(ZONE_IDS.webhook, 0, 0),
      codeNode(ZONE_IDS.zones, CODE_NODE_NAMES.zones, 260, 0, TIME_ZONE_SCRIPT),
    ],
    edges: [edge(ZONE_IDS.webhook, ZONE_IDS.zones)],
  };
}

const TWO_CODE_IDS = {
  webhook: "e2emonacowebhook000000000000001",
  codeA: "e2emonacocodea0000000000000001",
  codeB: "e2emonacocodeb0000000000000001",
};

/** blocks-logic#435 §10 fixture: a workflow with two Code nodes, Webhook → Code A → Code B. */
export function twoCodeNodesWorkflowFile(name: string): WorkflowImportFile {
  return {
    name,
    settings: {},
    nodes: [
      webhookNode(TWO_CODE_IDS.webhook, 0, 0),
      codeNode(TWO_CODE_IDS.codeA, CODE_NODE_NAMES.codeA, 260, 0, "return $items;\n"),
      codeNode(TWO_CODE_IDS.codeB, CODE_NODE_NAMES.codeB, 520, 0, "return $items;\n"),
    ],
    edges: [
      edge(TWO_CODE_IDS.webhook, TWO_CODE_IDS.codeA),
      edge(TWO_CODE_IDS.codeA, TWO_CODE_IDS.codeB),
    ],
  };
}
