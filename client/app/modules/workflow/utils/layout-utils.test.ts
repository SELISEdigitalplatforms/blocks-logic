import { describe, it, expect } from "vitest";
import type { Edge } from "@xyflow/react";
import { getLayoutedElements } from "./layout-utils";
import type { EditorNode } from "@blocks-workflow/models/node.model";

const node = (id: string) =>
  ({ id, position: { x: 0, y: 0 } }) as unknown as EditorNode;
const edge = (id: string, source: string, target: string) =>
  ({ id, source, target }) as unknown as Edge;

describe("getLayoutedElements", () => {
  it("returns the input unchanged when there are no nodes", () => {
    const empty = {};
    expect(getLayoutedElements(empty, {})).toBe(empty);
  });

  it("places nodes in increasing layers along the x axis", () => {
    const nodesMap = { a: node("a"), b: node("b"), c: node("c") };
    const edgesMap = {
      e1: edge("e1", "a", "b"),
      e2: edge("e2", "b", "c"),
    };
    const result = getLayoutedElements(nodesMap, edgesMap);

    // a is layer 0, b layer 1, c layer 2 -> strictly increasing x.
    expect(result.a.position.x).toBe(0);
    expect(result.b.position.x).toBeGreaterThan(result.a.position.x);
    expect(result.c.position.x).toBeGreaterThan(result.b.position.x);
  });

  it("centers sibling nodes in the same layer vertically", () => {
    // Two roots feeding one child: a and b share layer 0.
    const nodesMap = { a: node("a"), b: node("b"), c: node("c") };
    const edgesMap = {
      e1: edge("e1", "a", "c"),
      e2: edge("e2", "b", "c"),
    };
    const result = getLayoutedElements(nodesMap, edgesMap);

    expect(result.a.position.x).toBe(result.b.position.x);
    expect(result.a.position.y).not.toBe(result.b.position.y);
    // c sits one layer to the right of both roots.
    expect(result.c.position.x).toBeGreaterThan(result.a.position.x);
  });

  it("terminates on cyclic graphs without throwing", () => {
    const nodesMap = { a: node("a"), b: node("b") };
    const edgesMap = {
      e1: edge("e1", "a", "b"),
      e2: edge("e2", "b", "a"),
    };
    expect(() => getLayoutedElements(nodesMap, edgesMap)).not.toThrow();
  });
});
