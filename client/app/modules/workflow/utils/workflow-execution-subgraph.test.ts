import { describe, expect, it } from "vitest";
import {
  NodeExecutionStatus,
  buildExecutedSubgraph,
  getStatusStyles,
} from "./workflow-execution-editor.util";

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
