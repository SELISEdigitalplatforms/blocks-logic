import { describe, expect, it } from "vitest";
import { deriveMergeHandle, mergeInputCount } from "./merge-handles";

describe("deriveMergeHandle", () => {
  it.each([
    [null, 2],
    [undefined, 2],
    [Number.NaN, 2],
    ["3", 2],
    [0, 1],
    [-4, 1],
    [2.5, 2],
    [1, 1],
    [2, 2],
    [5, 5],
    [12, 12],
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
