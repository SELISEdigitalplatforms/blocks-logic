import { describe, it, expect } from "vitest";
import type { Edge } from "@xyflow/react";
import { getAllPredecessors } from "./predecessor.util";
import type { ExecutedItem } from "../models/workflow.model";
import type { EditorNode } from "@blocks-workflow/models/node.model";

const node = (id: string) => ({ id }) as unknown as EditorNode;
const edge = (id: string, source: string, target: string) =>
  ({ id, source, target }) as unknown as Edge;

const nodesMap: Record<string, EditorNode> = {
  a: node("a"),
  b: node("b"),
  c: node("c"),
  d: node("d"),
};

describe("getAllPredecessors", () => {
  describe("static graph (no execution data)", () => {
    it("returns direct and transitive predecessors", () => {
      // a -> b -> c
      const edgesMap = {
        e1: edge("e1", "a", "b"),
        e2: edge("e2", "b", "c"),
      };
      const result = getAllPredecessors("c", nodesMap, edgesMap);
      expect(result.map((n) => n.id).sort()).toEqual(["a", "b"]);
    });

    it("returns an empty array for a root node with no incoming edges", () => {
      const edgesMap = { e1: edge("e1", "a", "b") };
      expect(getAllPredecessors("a", nodesMap, edgesMap)).toEqual([]);
    });

    it("does not include the node itself even in a cycle", () => {
      // a -> b -> a (cycle)
      const edgesMap = {
        e1: edge("e1", "a", "b"),
        e2: edge("e2", "b", "a"),
      };
      const result = getAllPredecessors("a", nodesMap, edgesMap);
      expect(result.map((n) => n.id)).not.toContain("a");
      expect(result.map((n) => n.id)).toContain("b");
    });

    it("filters out predecessor ids missing from nodesMap", () => {
      const edgesMap = { e1: edge("e1", "ghost", "b") };
      expect(getAllPredecessors("b", nodesMap, edgesMap)).toEqual([]);
    });
  });

  describe("execution data path", () => {
    const item = (
      itemId: string,
      nodeId: string,
      parentItemIds: string[] | null,
    ) =>
      ({
        itemId,
        nodeId,
        parentItemIds,
        itemIndex: 0,
      }) as unknown as ExecutedItem;

    it("walks parentItemIds to build the predecessor set", () => {
      const executedItems = [
        item("i-a", "a", null),
        item("i-b", "b", ["i-a"]),
        item("i-c", "c", ["i-b"]),
      ];
      const result = getAllPredecessors("c", nodesMap, {}, executedItems);
      expect(result.map((n) => n.id).sort()).toEqual(["a", "b"]);
    });

    it("falls back to the static graph when the node has no executed items", () => {
      const executedItems = [item("i-a", "a", null)];
      const edgesMap = { e1: edge("e1", "a", "b") };
      const result = getAllPredecessors("b", nodesMap, edgesMap, executedItems);
      expect(result.map((n) => n.id)).toEqual(["a"]);
    });
  });
});
