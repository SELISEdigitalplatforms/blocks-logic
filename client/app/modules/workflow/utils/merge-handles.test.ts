import { describe, expect, it } from "vitest";
import { deriveMergeHandle, mergeInputCount, mergeInputCounts } from "./merge-handles";

describe("deriveMergeHandle", () => {
  it.each([
    [null, 2],
    [undefined, 2],
    [Number.NaN, 2],
    ["3", 2],
    [0, 2],
    [-4, 2],
    [2.5, 2],
    [1, 2],
    [2, 2],
    [5, 5],
    [10, 10],
    [12, 10],
  ])("C3: numberInputs %s gives %i inputs", (value, count) => {
    expect(mergeInputCount(value)).toBe(count);
    const handle = deriveMergeHandle({ numberInputs: value });
    expect(handle.targets).toHaveLength(count);
    expect(handle.sources).toEqual([{ key: "source", label: "" }]);
  });

  it("H7: inputs are merge-handle-target-1..N labelled Input 1..N", () => {
    expect(deriveMergeHandle({ numberInputs: 3 }).targets).toEqual([
      { key: "merge-handle-target-1", label: "Input 1" },
      { key: "merge-handle-target-2", label: "Input 2" },
      { key: "merge-handle-target-3", label: "Input 3" },
    ]);
  });
});

describe("mergeInputCounts", () => {
  const edge = (source: string, targetHandle: string | null, sourceHandle: string | null = "source") => ({ source, target: "m", sourceHandle, targetHandle });
  const counts: Record<string, Record<string, number>> = { a: { source: 2 }, b: { source: 1 }, check: { "if-true": 3, "if-false": 0 }, old: { source: 9 } };

  it("counts each input from its sources' branch counts, listing empty inputs too", () => {
    const edges = [edge("b", "merge-handle-target-2"), edge("a", "merge-handle-target-1"), { source: "x", target: "elsewhere", sourceHandle: "source", targetHandle: "merge-handle-target-1" }];
    expect(mergeInputCounts("m", 3, edges, (id) => counts[id])).toEqual([
      { label: "Input 1", count: 2 },
      { label: "Input 2", count: 1 },
      { label: "Input 3", count: 0 },
    ]);
  });

  it("adds two edges on one input, reads the edge's branch, treats a blank handle as Input 1 and ignores other handles", () => {
    const edges = [edge("a", "merge-handle-target-1"), edge("check", "merge-handle-target-1", "if-true"), edge("b", null, null), edge("old", "merge-handle-target-3"), edge("old", "input-2")];
    expect(mergeInputCounts("m", 2, edges, (id) => counts[id])).toEqual([
      { label: "Input 1", count: 6 },
      { label: "Input 2", count: 0 },
    ]);
  });

  it("a source that did not run counts 0", () => {
    expect(mergeInputCounts("m", 2, [edge("a", "merge-handle-target-1")], () => undefined)).toEqual([
      { label: "Input 1", count: 0 },
      { label: "Input 2", count: 0 },
    ]);
  });
});
