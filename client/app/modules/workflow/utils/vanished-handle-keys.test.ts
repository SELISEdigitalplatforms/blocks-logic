import { describe, expect, it } from "vitest";
import { vanishedHandleKeys } from "./vanished-handle-keys";

const handle = (sources: string[], targets: string[]) => ({
  sources: sources.map((key) => ({ key, label: "" })),
  targets: targets.map((key) => ({ key, label: "" })),
});

describe("vanishedHandleKeys", () => {
  it("lists the keys removed on each side", () => {
    expect(vanishedHandleKeys(handle(["a", "b", "c"], ["t1", "t2"]), handle(["a", "c"], ["t2"]))).toEqual({
      sources: ["b"],
      targets: ["t1"],
    });
  });

  it("ignores added and reordered keys", () => {
    expect(vanishedHandleKeys(handle(["a", "b"], []), handle(["b", "a", "z"], ["t"]))).toEqual({
      sources: [],
      targets: [],
    });
  });

  it("treats a missing handle as having no keys", () => {
    expect(vanishedHandleKeys(null, handle(["a"], []))).toEqual({ sources: [], targets: [] });
    expect(vanishedHandleKeys(handle(["a"], ["t"]), undefined)).toEqual({ sources: ["a"], targets: ["t"] });
  });
});
