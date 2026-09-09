import { describe, expect, it } from "vitest";
import {
  IMPORT_ERROR_MESSAGES,
  MAX_IMPORT_BYTES,
  importSuccessMessage,
  preflightWorkflowFile,
  remapAndSanitiseWorkflow,
  replaceIdsInString,
} from "./workflow-import";

const HEX32 = /^[0-9a-f]{32}$/;

const validNode = (id: string, extra: Record<string, unknown> = {}) => ({
  id,
  name: `Node ${id}`,
  type: "webhook",
  category: "trigger",
  version: "v1",
  position: { x: 1, y: 2 },
  parameters: {},
  settings: {},
  ...extra,
});

const rootOf = (over: Partial<Record<string, unknown>> = {}) => ({
  name: "wf",
  settings: {},
  nodes: [],
  edges: [],
  ...over,
});

const preflight = (obj: unknown) => {
  const text = JSON.stringify(obj);
  return preflightWorkflowFile({ text, size: text.length });
};

describe("preflightWorkflowFile", () => {
  it("V1: rejects files over 5 MB before parsing (C1)", () => {
    const res = preflightWorkflowFile({ text: "{}", size: MAX_IMPORT_BYTES + 1 });
    expect(res).toMatchObject({ ok: false, code: "IMPORT_TOO_LARGE" });
    expect((res as { message: string }).message).toBe(IMPORT_ERROR_MESSAGES.IMPORT_TOO_LARGE);
  });

  it("V2: rejects non-JSON text (C2)", () => {
    const res = preflightWorkflowFile({ text: "not json", size: 8 });
    expect(res).toMatchObject({ ok: false, code: "IMPORT_NOT_JSON" });
  });

  it("V2: rejects a JSON array / null root (C2)", () => {
    expect(preflight([])).toMatchObject({ ok: false, code: "IMPORT_NOT_JSON" });
    expect(preflight(null)).toMatchObject({ ok: false, code: "IMPORT_NOT_JSON" });
  });

  it("V3: rejects a missing / blank name (C3)", () => {
    expect(preflight({ foo: 1 })).toMatchObject({ ok: false, code: "IMPORT_BAD_SHAPE" });
    expect(preflight(rootOf({ name: "   " }))).toMatchObject({
      ok: false,
      code: "IMPORT_BAD_SHAPE",
    });
  });

  it("V4/V5: rejects non-array nodes / edges (C3)", () => {
    expect(preflight(rootOf({ nodes: {} }))).toMatchObject({ ok: false, code: "IMPORT_BAD_SHAPE" });
    expect(preflight(rootOf({ edges: "x" }))).toMatchObject({ ok: false, code: "IMPORT_BAD_SHAPE" });
  });

  it("V6: rejects non-object settings (C3)", () => {
    expect(preflight(rootOf({ settings: [] }))).toMatchObject({
      ok: false,
      code: "IMPORT_BAD_SHAPE",
    });
    expect(preflight(rootOf({ settings: null }))).toMatchObject({
      ok: false,
      code: "IMPORT_BAD_SHAPE",
    });
  });

  it("passes a well-formed file and defaults to '' (Example 4)", () => {
    const res = preflight({ name: "Blank", settings: {}, nodes: [], edges: [] });
    expect(res.ok).toBe(true);
  });
});

describe("replaceIdsInString", () => {
  const map = new Map([["8d75404dc42646619b91a7d85278687b", "NEWID"]]);

  it("replaces a whole-token id", () => {
    expect(replaceIdsInString("8d75404dc42646619b91a7d85278687b", map)).toBe("NEWID");
  });

  it("replaces ids embedded as node_<id>_<handle> refs (D3)", () => {
    expect(replaceIdsInString("node_8d75404dc42646619b91a7d85278687b_source", map)).toBe(
      "node_NEWID_source",
    );
  });

  it("does not replace a chance substring inside a longer alphanumeric run", () => {
    expect(replaceIdsInString("zz8d75404dc42646619b91a7d85278687bzz", map)).toBe(
      "zz8d75404dc42646619b91a7d85278687bzz",
    );
  });
});

describe("remapAndSanitiseWorkflow", () => {
  it("regenerates every node id as fresh 32-char hex and rewrites the webhook path (H6, Example 2)", () => {
    const root = rootOf({
      nodes: [
        validNode("8d75404dc42646619b91a7d85278687b", {
          parameters: { path: "8d75404dc42646619b91a7d85278687b" },
        }),
        validNode("bcc3fabc012345678901234567890abc"),
      ],
      edges: [
        {
          id: "xy-edge__8d75404dc42646619b91a7d85278687b-bcc3fabc012345678901234567890abc",
          source: "8d75404dc42646619b91a7d85278687b",
          target: "bcc3fabc012345678901234567890abc",
          sourceHandle: "source",
          targetHandle: "target",
        },
      ],
    });

    const out = remapAndSanitiseWorkflow(root as never);

    expect(out.issues).toBe(0);
    expect(out.nodes).toHaveLength(2);
    const newWebhookId = out.nodes[0].id as string;
    const newHttpId = out.nodes[1].id as string;
    expect(newWebhookId).toMatch(HEX32);
    expect(newHttpId).toMatch(HEX32);
    expect(newWebhookId).not.toBe("8d75404dc42646619b91a7d85278687b");
    expect((out.nodes[0].parameters as { path: string }).path).toBe(newWebhookId);

    expect(out.edges).toHaveLength(1);
    expect(out.edges[0]).toMatchObject({
      id: `xy-edge__${newWebhookId}-${newHttpId}`,
      source: newWebhookId,
      target: newHttpId,
      sourceHandle: "source",
      targetHandle: "target",
    });
  });

  it("rewrites whole-token ids inside workflow-level settings values (H6)", () => {
    const root = rootOf({
      nodes: [validNode("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")],
      settings: { entry: "node_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa_source", flag: true },
    });
    const out = remapAndSanitiseWorkflow(root as never);
    const newId = out.nodes[0].id as string;
    expect(out.settings.entry).toBe(`node_${newId}_source`);
    expect(out.settings.flag).toBe(true);
  });

  it("drops malformed nodes and counts them as issues (C4)", () => {
    const root = rootOf({
      nodes: [
        validNode("11111111111111111111111111111111"),
        { id: "x", name: "bad" }, // missing type/category/version/position
        "not-an-object",
        validNode("22222222222222222222222222222222", { position: { x: "nope", y: 1 } }),
      ],
    });
    const out = remapAndSanitiseWorkflow(root as never);
    expect(out.nodes).toHaveLength(1);
    expect(out.issues).toBe(3);
  });

  it("de-duplicates node ids keeping the first, and drops dangling edges (C4, C5, Example 5)", () => {
    const root = rootOf({
      nodes: [
        validNode("x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1", { name: "A" }),
        validNode("x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1", { name: "B" }),
        validNode("x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2", { name: "C" }),
      ],
      edges: [
        {
          source: "x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1x1",
          target: "x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2",
          sourceHandle: "source",
          targetHandle: "target",
        },
        {
          source: "x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2x2",
          target: "x9x9x9x9x9x9x9x9x9x9x9x9x9x9x9x9",
          sourceHandle: "source",
          targetHandle: "target",
        },
      ],
    });
    const out = remapAndSanitiseWorkflow(root as never);
    expect(out.nodes).toHaveLength(2);
    expect(out.edges).toHaveLength(1);
    expect(out.issues).toBe(2);
    expect(importSuccessMessage(out.issues)).toBe(
      "Workflow imported. 2 item(s) were skipped because they were invalid or disconnected.",
    );
  });

  it("suffixes a regenerated edge id that collides with another kept edge", () => {
    const root = rootOf({
      nodes: [
        validNode("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"),
        validNode("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"),
      ],
      edges: [
        { source: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", target: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", sourceHandle: "s", targetHandle: "t" },
        { source: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", target: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", sourceHandle: "s2", targetHandle: "t2" },
      ],
    });
    const out = remapAndSanitiseWorkflow(root as never);
    expect(out.edges).toHaveLength(2);
    expect(out.edges[0].id).not.toBe(out.edges[1].id);
    expect(String(out.edges[1].id).endsWith("-1")).toBe(true);
  });

  it("handles an empty workflow (Example 4)", () => {
    const out = remapAndSanitiseWorkflow(rootOf() as never);
    expect(out).toEqual({ nodes: [], edges: [], settings: {}, issues: 0 });
    expect(importSuccessMessage(0)).toBe("Workflow imported.");
  });
});
