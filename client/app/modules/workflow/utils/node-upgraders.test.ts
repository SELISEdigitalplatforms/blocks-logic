import { afterAll, beforeAll, describe, expect, it } from "vitest";
import type { WorkflowNodeDefinition, WorkflowNode } from "../models/node.model";
import { NodeDefinitions, getNodeDefinition, servesVersion } from "../components/node-library-panel/node-definitions";
import { cloneHandle, upgradeNode } from "./node-upgraders";

// Handle ids each type had on dev before SPEC5; saved edges reference these.
const OLD_HANDLE_IDS: Record<string, { source: string[]; target: string[] }> = {
  webhook: { source: ["source"], target: [] },
  email: { source: ["source"], target: [] },
  dataGateway: { source: ["source"], target: [] },
  schedule: { source: ["source"], target: [] },
  agent: { source: ["source"], target: ["target"] },
  sendMail: { source: ["source"], target: ["target"] },
  httpRequest: { source: ["source"], target: ["target"] },
  proxy: { source: ["source"], target: ["target"] },
  dataAction: { source: ["source"], target: ["target"] },
  function: { source: ["source"], target: ["target"] },
  if: { source: ["if-true", "if-false"], target: ["target"] },
  setfield: { source: ["source"], target: ["target"] },
  code: { source: ["source"], target: ["target"] },
};

const IF_HANDLE = {
  sources: [
    { key: "if-true", label: "True" },
    { key: "if-false", label: "False" },
  ],
  targets: [{ key: "target", label: "" }],
};

const SIMPLE_HANDLE = {
  sources: [{ key: "source", label: "" }],
  targets: [{ key: "target", label: "" }],
};

const TRIGGER_HANDLE = { sources: [{ key: "source", label: "" }], targets: [] };

const node = (extra: Partial<WorkflowNode> & Record<string, unknown> = {}): WorkflowNode =>
  ({
    id: "n1",
    name: "If",
    category: "logic",
    type: "if",
    version: "v1",
    position: { x: 1, y: 2 },
    data: {},
    parameters: { conditions: [] },
    settings: {},
    handle: null,
    ...extra,
  }) as WorkflowNode;

describe("upgradeNode", () => {
  it("H1: upgrades a v1 If node to v2 with the template handle and today's keys", () => {
    const upgraded = upgradeNode(node());
    expect(upgraded.version).toBe("v2");
    expect(upgraded.handle).toEqual(IF_HANDLE);
    expect(upgraded.parameters).toEqual({ conditions: [] });
    expect(upgraded.position).toEqual({ x: 1, y: 2 });
  });

  it("H1: every one of the 13 types upgrades to v2 with keys identical to its old handle ids", () => {
    expect(NodeDefinitions.map((d) => d.type).sort()).toEqual(Object.keys(OLD_HANDLE_IDS).sort());
    for (const definition of NodeDefinitions) {
      const upgraded = upgradeNode(
        node({ category: definition.category, type: definition.type, version: "v1" }),
      );
      expect(upgraded.version).toBe("v2");
      expect(upgraded.handle?.sources.map((e) => e.key)).toEqual(OLD_HANDLE_IDS[definition.type].source);
      expect(upgraded.handle?.targets.map((e) => e.key)).toEqual(OLD_HANDLE_IDS[definition.type].target);
    }
  });

  it("gives triggers a source and no target, and simple nodes source + target", () => {
    expect(upgradeNode(node({ category: "trigger", type: "webhook" })).handle).toEqual(TRIGGER_HANDLE);
    expect(upgradeNode(node({ category: "transform", type: "code" })).handle).toEqual(SIMPLE_HANDLE);
  });

  it("does not mutate the stored node", () => {
    const stored = node();
    const copy = structuredClone(stored);
    upgradeNode(stored);
    expect(stored).toEqual(copy);
  });

  it.each([undefined, "", "1", "1.0", "V1", "latest"])(
    "C1: reads version %j as v1 and upgrades it",
    (version) => {
      const upgraded = upgradeNode(node({ version: version as WorkflowNode["version"] }));
      expect(upgraded.version).toBe("v2");
      expect(upgraded.handle).toEqual(IF_HANDLE);
    },
  );

  it("C2: leaves a node without a matching definition unchanged", () => {
    const unknownType = node({ type: "manual" as WorkflowNode["type"], category: "trigger" });
    expect(upgradeNode(unknownType)).toBe(unknownType);

    const futureVersion = node({ version: "v3" });
    expect(upgradeNode(futureVersion)).toBe(futureVersion);
  });

  it("C7: keeps a stored v2 handle and is idempotent", () => {
    const custom = {
      sources: [
        { key: "if-true", label: "Yes" },
        { key: "if-false", label: "No" },
      ],
      targets: [{ key: "target", label: "" }],
    };
    const stored = node({ version: "v2", handle: custom });
    const once = upgradeNode(stored);
    expect(once).toBe(stored);
    expect(once.handle).toEqual(custom);
    expect(upgradeNode(upgradeNode(node()))).toEqual(upgradeNode(node()));
  });

  it.each([
    ["null", null],
    ["missing", undefined],
    ["empty", { sources: [], targets: [] }],
    ["without lists", {}],
  ])("C13: fills a %s handle on a v2 node from the template", (_label, handle) => {
    const upgraded = upgradeNode(node({ version: "v2", handle: handle as WorkflowNode["handle"] }));
    expect(upgraded.version).toBe("v2");
    expect(upgraded.handle).toEqual(IF_HANDLE);
  });

  it("labels: If outputs are True/False and every other handle has no label, as before", () => {
    for (const definition of NodeDefinitions) {
      for (const entry of [...definition.handle.sources, ...definition.handle.targets]) {
        const expected = entry.key === "if-true" ? "True" : entry.key === "if-false" ? "False" : "";
        expect(entry.label).toBe(expected);
      }
    }
  });

  it("every definition is created at v2", () => {
    for (const definition of NodeDefinitions) {
      expect(definition.version).toBe("v2");
    }
  });

  it("C11: gives each node its own copy of the template handle", () => {
    const a = upgradeNode(node({ id: "a" }));
    const b = upgradeNode(node({ id: "b" }));
    a.handle!.sources[0].label = "changed";
    expect(b.handle!.sources[0].label).toBe("True");
    expect(NodeDefinitions.find((d) => d.type === "if")!.handle.sources[0].label).toBe("True");
  });

  describe("C14: a definition with deriveHandle", () => {
    const derived: WorkflowNodeDefinition = {
      ...NodeDefinitions.find((d) => d.type === "code")!,
      id: "logic-setfield-v1",
      category: "logic",
      type: "setfield",
      deriveHandle: (parameters) => ({
        sources: ((parameters?.outputs as string[]) ?? []).map((key) => ({ key, label: key })),
        targets: [{ key: "target", label: "" }],
      }),
    };
    beforeAll(() => {
      NodeDefinitions.push(derived);
    });
    afterAll(() => {
      NodeDefinitions.splice(NodeDefinitions.indexOf(derived), 1);
    });

    it("rebuilds the handle from the parameters on every load, whatever is stored", () => {
      const stored = node({
        category: "logic",
        type: "setfield",
        version: "v2",
        parameters: { outputs: ["a", "b"] },
        handle: { sources: [{ key: "stale", label: "" }], targets: [] },
      });
      expect(upgradeNode(stored).handle).toEqual({
        sources: [
          { key: "a", label: "a" },
          { key: "b", label: "b" },
        ],
        targets: [{ key: "target", label: "" }],
      });
    });

    it("derives for a v1 node too", () => {
      const upgraded = upgradeNode(
        node({ category: "logic", type: "setfield", parameters: { outputs: ["x"] } }),
      );
      expect(upgraded.version).toBe("v2");
      expect(upgraded.handle?.sources).toEqual([{ key: "x", label: "x" }]);
    });
  });
});

describe("servesVersion / getNodeDefinition", () => {
  it("a v2 definition serves stored v1 and v2 only", () => {
    expect(servesVersion("v2", "v1")).toBe(true);
    expect(servesVersion("v2", "v2")).toBe(true);
    for (const version of ["v3", "v0", "1.0", "1", "V1", "", "v", "v1.0", "latest"]) {
      expect(servesVersion("v2", version)).toBe(false);
    }
  });

  it("finds the same definition for v1 and v2, none for v3", () => {
    expect(getNodeDefinition("logic", "if", "v1")).toBe(getNodeDefinition("logic", "if", "v2"));
    expect(getNodeDefinition("logic", "if", "v1")?.type).toBe("if");
    expect(getNodeDefinition("logic", "if", "v3")).toBeUndefined();
    expect(getNodeDefinition("action", "if", "v1")).toBeUndefined();
  });
});

describe("cloneHandle", () => {
  it("deep copies and fills missing lists", () => {
    const copy = cloneHandle(IF_HANDLE);
    expect(copy).toEqual(IF_HANDLE);
    expect(copy.sources[0]).not.toBe(IF_HANDLE.sources[0]);
    expect(cloneHandle({} as never)).toEqual({ sources: [], targets: [] });
  });
});
