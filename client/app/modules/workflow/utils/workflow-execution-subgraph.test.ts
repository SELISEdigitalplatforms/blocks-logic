import { describe, expect, it } from "vitest";
import {
  NodeExecutionStatus,
  buildExecutedSubgraph,
  getStatusStyles,
} from "./workflow-execution-editor.util";
import { upgradeNode } from "./node-upgraders";

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const node = (id: string): any => ({ id, name: id, position: { x: 0, y: 0 } });
const edge = (
  id: string,
  source: string,
  target: string,
  sourceHandle?: string,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
): any => ({ id, source, target, sourceHandle });
const exec = (
  nodeId: string,
  extra: Record<string, unknown> = {},
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
): any => ({
  nodeId,
  status: NodeExecutionStatus.Completed,
  inputItemCount: 0,
  outputItemCount: 0,
  ...extra,
});

describe("getStatusStyles", () => {
  it("returns distinct styles per status and a default", () => {
    expect(getStatusStyles(NodeExecutionStatus.Pending).edgeColor).toBe(
      "#eab308",
    );
    expect(getStatusStyles(NodeExecutionStatus.Running).edgeColor).toBe(
      "#a855f7",
    );
    expect(getStatusStyles(NodeExecutionStatus.Completed).nodeClass).toBe(
      "border-success",
    );
    expect(getStatusStyles(NodeExecutionStatus.Failed).nodeClass).toBe(
      "border-destructive",
    );
    expect(getStatusStyles(999).edgeColor).toBe("#94a3b8");
  });
});

describe("buildExecutedSubgraph", () => {
  it("marks a trigger node and follows a non-branching edge", () => {
    const nodes = [node("t"), node("a")];
    const edges = [edge("e1", "t", "a")];
    const execs = [
      exec("t", { outputItemCount: 1 }),
      exec("a", { inputItemCount: 1 }),
    ];
    const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(
      nodes,
      edges,
      execs,
      [{ nodeId: "a" }] as never,
    );
    expect(reachableNodeIds.has("t")).toBe(true);
    expect(reachableNodeIds.has("a")).toBe(true);
    expect(traversedEdgeIds.has("e1")).toBe(true);
  });

  it("follows the true branch using the mapped branch counts", () => {
    const nodes = [node("t"), node("yes"), node("no")];
    const edges = [
      edge("e-true", "t", "yes", "if-true"),
      edge("e-false", "t", "no", "if-false"),
    ];
    const execs = [
      exec("t", {
        outputItemCount: 1,
        outputCountsByBranch: { True: 1, False: 0 },
      }),
      exec("yes"),
      exec("no"),
    ];
    const { traversedEdgeIds } = buildExecutedSubgraph(
      nodes,
      edges,
      execs,
      [] as never,
    );
    expect(traversedEdgeIds.has("e-true")).toBe(true);
    expect(traversedEdgeIds.has("e-false")).toBe(false);
  });

  it("falls back to the raw handle id in branch counts", () => {
    const nodes = [node("t"), node("yes")];
    const edges = [edge("e-true", "t", "yes", "if-true")];
    const execs = [
      exec("t", {
        outputItemCount: 1,
        outputCountsByBranch: { "if-true": 2 },
      }),
      exec("yes"),
    ];
    const { traversedEdgeIds } = buildExecutedSubgraph(
      nodes,
      edges,
      execs,
      [] as never,
    );
    expect(traversedEdgeIds.has("e-true")).toBe(true);
  });

  it("falls back to whether the target executed when no branch counts exist", () => {
    const nodes = [node("t"), node("yes")];
    const edges = [edge("e-true", "t", "yes", "if-true")];
    const execs = [
      exec("t", { outputItemCount: 1 }),
      exec("yes", { inputItemCount: 1 }),
    ];
    const { traversedEdgeIds } = buildExecutedSubgraph(
      nodes,
      edges,
      execs,
      [{ nodeId: "yes" }] as never,
    );
    expect(traversedEdgeIds.has("e-true")).toBe(true);
  });

  it("stops the BFS at a failed node", () => {
    const nodes = [node("t"), node("a"), node("b")];
    const edges = [edge("e1", "t", "a"), edge("e2", "a", "b")];
    const execs = [
      exec("t", { outputItemCount: 1 }),
      exec("a", { inputItemCount: 1, status: NodeExecutionStatus.Failed }),
      exec("b"),
    ];
    const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(
      nodes,
      edges,
      execs,
      [{ nodeId: "a" }] as never,
    );
    expect(reachableNodeIds.has("a")).toBe(true);
    // downstream of the failure is not traversed
    expect(traversedEdgeIds.has("e2")).toBe(false);
    expect(reachableNodeIds.has("b")).toBe(false);
  });

  it("returns empty sets when nothing executed", () => {
    const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(
      [node("t")],
      [],
      [],
      [] as never,
    );
    expect(reachableNodeIds.size).toBe(0);
    expect(traversedEdgeIds.size).toBe(0);
  });
});

describe("buildExecutedSubgraph with per-node handles (SPEC5)", () => {
  // A stored v1 snapshot If node (no handle), upgraded the way the execution view does it.
  const ifNode = (id: string) =>
    upgradeNode({ id, name: id, category: "logic", type: "if", version: "v1", position: { x: 0, y: 0 }, data: {} } as never);
  const codeNode = (id: string) =>
    upgradeNode({ id, name: id, category: "transform", type: "code", version: "v1", position: { x: 0, y: 0 }, data: {} } as never);

  it("H8: colours only the taken If branch for a v1 snapshot without a handle", () => {
    const nodes = [codeNode("t"), ifNode("if"), codeNode("yes"), codeNode("no")];
    const edges = [
      edge("e0", "t", "if", "source"),
      edge("e-true", "if", "yes", "if-true"),
      edge("e-false", "if", "no", "if-false"),
    ];
    const execs = [
      exec("t", { outputItemCount: 1 }),
      exec("if", { inputItemCount: 1, outputItemCount: 1, outputCountsByBranch: { True: 0, False: 1 } }),
      exec("no", { inputItemCount: 1 }),
    ];
    const { reachableNodeIds, traversedEdgeIds } = buildExecutedSubgraph(nodes, edges, execs, [] as never);
    expect([...traversedEdgeIds].sort()).toEqual(["e-false", "e0"]);
    expect(reachableNodeIds.has("no")).toBe(true);
    expect(reachableNodeIds.has("yes")).toBe(false);
  });

  it("keeps a single-output edge on the default key always traversed", () => {
    const nodes = [codeNode("t"), codeNode("a")];
    const execs = [exec("t", { outputItemCount: 1, outputCountsByBranch: { source: 0 } }), exec("a")];
    const { traversedEdgeIds } = buildExecutedSubgraph(nodes, [edge("e1", "t", "a", "source")], execs, [] as never);
    expect(traversedEdgeIds.has("e1")).toBe(true);
  });

  it("treats a single output with its own key as branching", () => {
    const single = {
      ...codeNode("s"),
      handle: { sources: [{ key: "rule-1", label: "" }], targets: [{ key: "target", label: "" }] },
    };
    const nodes = [single, codeNode("a")];
    const execs = [exec("s", { outputItemCount: 1, outputCountsByBranch: { "rule-1": 0 } }), exec("a")];
    const { traversedEdgeIds } = buildExecutedSubgraph(nodes, [edge("e1", "s", "a", "rule-1")], execs, [] as never);
    expect(traversedEdgeIds.has("e1")).toBe(false);
  });
});

describe("buildExecutedSubgraph with a Switch (SPEC6)", () => {
  const A = "switch-handle-source-aaaaa1";
  const B = "switch-handle-source-bbbbb2";
  const sw = (rules: string[]) =>
    upgradeNode({
      id: "s", name: "s", category: "logic", type: "switch", version: "v2", position: { x: 0, y: 0 }, data: {},
      parameters: { mode: "rules", rules: rules.map((handleKey) => ({ handleKey, handleLabel: "", conditionType: "all", conditions: [] })), options: { fallbackOutput: "none" } },
    } as never);
  const plain = (id: string) => ({ id, name: id, position: { x: 0, y: 0 } }) as never;

  it("H13: traverses only the keys that carried items", () => {
    const nodes = [plain("t"), sw([A, B]), plain("a"), plain("b")];
    const edges = [edge("e0", "t", "s", "source"), edge("ea", "s", "a", A), edge("eb", "s", "b", B)];
    const execs = [exec("t", { outputItemCount: 1 }), exec("s", { inputItemCount: 1, outputItemCount: 1, outputCountsByBranch: { [B]: 1 } }), exec("b", { inputItemCount: 1 })];
    const { traversedEdgeIds } = buildExecutedSubgraph(nodes, edges, execs, [] as never);
    expect([...traversedEdgeIds].sort()).toEqual(["e0", "eb"]);
  });

  it("H13: a Switch with a single output that carried nothing is not traversed", () => {
    const nodes = [plain("t"), sw([A]), plain("a")];
    const edges = [edge("e0", "t", "s", "source"), edge("ea", "s", "a", A)];
    const execs = [exec("t", { outputItemCount: 1 }), exec("s", { inputItemCount: 1, outputItemCount: 0, outputCountsByBranch: {} }), exec("a")];
    const { traversedEdgeIds } = buildExecutedSubgraph(nodes, edges, execs, [] as never);
    expect(traversedEdgeIds.has("ea")).toBe(false);
  });
});
