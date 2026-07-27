import { describe, it, expect } from "vitest";
import {
  getOrderedNodeData,
  inferSchemaFromRuntimeRows,
} from "./runtime-node-data";
import type { ExecutedItem } from "@blocks-workflow/models/workflow.model";

const item = (
  nodeId: string,
  itemIndex: number,
  data: Record<string, unknown>,
) => ({ nodeId, itemIndex, data }) as unknown as ExecutedItem;

describe("getOrderedNodeData", () => {
  it("filters by nodeId, orders by itemIndex and maps the requested data key", () => {
    const items = [
      item("n1", 2, { Output: { v: 2 } }),
      item("n2", 0, { Output: { v: 99 } }),
      item("n1", 0, { Output: { v: 0 } }),
      item("n1", 1, { Output: { v: 1 } }),
    ];
    expect(getOrderedNodeData(items, "n1", "Output")).toEqual([
      { v: 0 },
      { v: 1 },
      { v: 2 },
    ]);
  });

  it("returns an empty array when no items match", () => {
    expect(getOrderedNodeData([], "n1", "Input")).toEqual([]);
  });

  it("yields undefined for rows missing the requested key", () => {
    const items = [item("n1", 0, { Input: { a: 1 } })];
    expect(getOrderedNodeData(items, "n1", "Output")).toEqual([undefined]);
  });
});

describe("inferSchemaFromRuntimeRows", () => {
  it("infers field types from the first-seen rows preserving key order", () => {
    const schema = inferSchemaFromRuntimeRows([
      { name: "x", age: 3, active: true, tags: [], meta: {} },
    ]);
    expect(schema).toEqual([
      { key: "name", type: "string" },
      { key: "age", type: "number" },
      { key: "active", type: "boolean" },
      { key: "tags", type: "array" },
      { key: "meta", type: "object" },
    ]);
  });

  it("skips non-record rows", () => {
    expect(inferSchemaFromRuntimeRows([null, 5, "str", undefined])).toEqual([]);
  });

  it("collapses conflicting primitive types to string", () => {
    const schema = inferSchemaFromRuntimeRows([{ v: 1 }, { v: "text" }]);
    expect(schema).toEqual([{ key: "v", type: "string" }]);
  });

  it("merges array and object into object", () => {
    const schema = inferSchemaFromRuntimeRows([{ v: [] }, { v: {} }]);
    expect(schema).toEqual([{ key: "v", type: "object" }]);
  });

  it("defaults to string when a key only ever holds null", () => {
    const schema = inferSchemaFromRuntimeRows([{ v: null }]);
    expect(schema).toEqual([{ key: "v", type: "string" }]);
  });
});
