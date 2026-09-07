import { describe, expect, it } from "vitest";
import {
  buildWorkflowExport,
  slugifyWorkflowName,
  workflowExportFileName,
} from "./workflow-export";
import { Workflow } from "../models/workflow.model";

const sourceWorkflow = (): Workflow =>
  ({
    name: "wf",
    description: "",
    settings: {},
    // audit / publish / execution fields that must never be exported
    itemId: "c17be424",
    tenantId: "t1",
    organizationId: "o1",
    isPublished: true,
    isDirty: true,
    publishedVersion: "v3",
    publishedVersionId: "pv3",
    createdBy: "u1",
    createdDate: "2026-01-01",
    lastUpdatedBy: "u2",
    lastUpdatedDate: "2026-02-02",
    language: "en",
    tags: [],
    items: [{ itemId: "x" }],
    nodeExecutions: [{ id: "n" }],
    nodes: [
      {
        id: "8d75404dc42646619b91a7d85278687b",
        name: "Webhook",
        category: "trigger",
        type: "webhook",
        version: "v1",
        position: { x: 10, y: 20 },
        handle: { source: ["source"] },
        parameters: { path: "8d75404dc42646619b91a7d85278687b" },
        settings: {},
        pinData: [{ json: { a: 1 } }],
        // extra runtime junk that should be dropped
        isComplete: true,
      },
      {
        id: "bcc3fabc",
        name: "HTTP Request",
        category: "action",
        type: "httpRequest",
        version: "v1",
        position: { x: 200, y: 20 },
        parameters: {},
        settings: {},
      },
    ],
    edges: [
      {
        id: "xy-edge__8d75404dc42646619b91a7d85278687b-bcc3fabc",
        source: "8d75404dc42646619b91a7d85278687b",
        target: "bcc3fabc",
        sourceHandle: "source",
        targetHandle: "target",
        type: "smoothstep",
      },
    ],
  }) as unknown as Workflow;

describe("buildWorkflowExport", () => {
  it("keeps only name, description, settings, nodes, edges", () => {
    const out = buildWorkflowExport(sourceWorkflow());
    expect(Object.keys(out).sort()).toEqual(
      ["description", "edges", "name", "nodes", "settings"].sort(),
    );
  });

  it("strips itemId / tenant / publish / audit / execution fields (H3)", () => {
    const out = JSON.stringify(buildWorkflowExport(sourceWorkflow()));
    for (const forbidden of [
      "itemId",
      "tenantId",
      "organizationId",
      "isPublished",
      "isDirty",
      "publishedVersion",
      "createdBy",
      "createdDate",
      "lastUpdatedBy",
      "lastUpdatedDate",
      "language",
      "tags",
      "nodeExecutions",
    ]) {
      expect(out).not.toContain(forbidden);
    }
  });

  it("defaults description to '' and settings to {} when absent", () => {
    const out = buildWorkflowExport({ name: "x", nodes: [], edges: [] } as unknown as Workflow);
    expect(out.description).toBe("");
    expect(out.settings).toEqual({});
  });

  it("retains node pinData verbatim (D6) and copies parameters/settings", () => {
    const out = buildWorkflowExport(sourceWorkflow());
    expect(out.nodes[0].pinData).toEqual([{ json: { a: 1 } }]);
    expect(out.nodes[1].pinData).toBeNull();
    expect(out.nodes[0].parameters).toEqual({ path: "8d75404dc42646619b91a7d85278687b" });
  });

  it("drops non-contract node fields such as isComplete", () => {
    const out = buildWorkflowExport(sourceWorkflow());
    expect("isComplete" in out.nodes[0]).toBe(false);
    expect(out.nodes[0].handle).toEqual({ source: ["source"] });
  });

  it("preserves extra xyflow edge props", () => {
    const out = buildWorkflowExport(sourceWorkflow());
    expect(out.edges[0].type).toBe("smoothstep");
    expect(out.edges[0].sourceHandle).toBe("source");
  });
});

describe("slugifyWorkflowName", () => {
  it("lowercases, collapses non-alphanumerics and trims dashes", () => {
    expect(slugifyWorkflowName("  My Cool Workflow!! ")).toBe("my-cool-workflow");
    expect(slugifyWorkflowName("WF")).toBe("wf");
    expect(slugifyWorkflowName("***")).toBe("");
  });
});

describe("workflowExportFileName", () => {
  it("builds <slug>-<YYYY-MM-DD>.json (H2 / D7)", () => {
    expect(workflowExportFileName("wf", new Date(2026, 8, 7))).toBe("wf-2026-09-07.json");
  });

  it("falls back to 'workflow' when the slug is empty", () => {
    expect(workflowExportFileName("!!!", new Date(2026, 0, 5))).toBe("workflow-2026-01-05.json");
  });
});
