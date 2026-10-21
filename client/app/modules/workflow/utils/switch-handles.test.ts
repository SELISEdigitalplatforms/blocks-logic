import { afterEach, describe, expect, it, vi } from "vitest";
import {
  SWITCH_FALLBACK_KEY,
  defaultSwitchParameters,
  deriveSwitchHandle,
  edgesOffKeys,
  generateSwitchKey,
  isSwitchKey,
  switchHandleLabel,
} from "./switch-handles";

const A = "switch-handle-source-aaaaa1";
const B = "switch-handle-source-bbbbb2";

const rules = [
  { key: A, outputName: "Paid", conditionType: "all", conditions: [] },
  { key: B, outputName: "", conditionType: "all", conditions: [] },
];

describe("deriveSwitchHandle", () => {
  it("H1/H12: rules mode lists the rules in order, with one target", () => {
    expect(deriveSwitchHandle({ mode: "rules", rules })).toEqual({
      targets: [{ key: "target", label: "" }],
      sources: [
        { key: A, label: "Paid" },
        { key: B, label: "" },
      ],
    });
  });

  it("H7: Extra output adds the fallback last, labelled with its rename", () => {
    const handle = deriveSwitchHandle({ mode: "rules", rules, options: { fallbackOutput: "extra", renameFallbackOutput: "Other" } });
    expect(handle.sources.at(-1)).toEqual({ key: SWITCH_FALLBACK_KEY, label: "Other" });
    expect(deriveSwitchHandle({ mode: "rules", rules, options: { fallbackOutput: A } }).sources).toHaveLength(2);
  });

  it("lists the rules even when a leftover mode or outputs list is stored", () => {
    expect(deriveSwitchHandle({ mode: "expression", rules, outputs: [{ key: "switch-handle-source-ooooo1", outputName: "" }] }).sources).toEqual([
      { key: A, label: "Paid" },
      { key: B, label: "" },
    ]);
  });

  it("treats missing or broken parameters as no outputs", () => {
    expect(deriveSwitchHandle(undefined).sources).toEqual([]);
    expect(deriveSwitchHandle({ rules: "x" }).sources).toEqual([]);
    expect(deriveSwitchHandle({ rules: [null, rules[0]] }).sources).toEqual([{ key: A, label: "Paid" }]);
  });
});

describe("switchHandleLabel", () => {
  const handle = deriveSwitchHandle({ mode: "rules", rules, options: { fallbackOutput: "extra", renameFallbackOutput: "" } });

  it("A13: shows the name, or Output <0-based index>, or Fallback", () => {
    expect(switchHandleLabel(handle, A)).toBe("Paid");
    expect(switchHandleLabel(handle, B)).toBe("Output 1");
    expect(switchHandleLabel(handle, SWITCH_FALLBACK_KEY)).toBe("Fallback");
    expect(switchHandleLabel(handle, "unknown")).toBe("");
    expect(switchHandleLabel(handle, null)).toBe("");
  });
});

describe("keys", () => {
  afterEach(() => vi.restoreAllMocks());

  it("C11: new keys match the server format", () => {
    for (let i = 0; i < 200; i++) expect(isSwitchKey(generateSwitchKey([]))).toBe(true);
  });

  it("C11: regenerates on a collision", () => {
    const fill = (value: number) => (array: Uint8Array) => {
      array.fill(value);
      return array;
    };
    vi.spyOn(crypto, "getRandomValues")
      .mockImplementationOnce(fill(0) as never)
      .mockImplementationOnce(fill(1) as never);
    expect(generateSwitchKey(["switch-handle-source-aaaaaa"])).toBe("switch-handle-source-bbbbbb");
  });

  it("the fallback key is not a rule key", () => {
    expect(isSwitchKey(SWITCH_FALLBACK_KEY)).toBe(false);
  });

  it("defaults are fresh for every node", () => {
    const a = defaultSwitchParameters();
    const b = defaultSwitchParameters();
    expect(a.rules).toHaveLength(1);
    expect(a.rules[0].key).not.toBe(b.rules[0].key);
    expect(a.options).not.toBe(b.options);
  });
});

describe("edgesOffKeys", () => {
  it("lists only that node's edges on keys outside the given set", () => {
    const edges = [
      { id: "1", source: "s", target: "x", sourceHandle: A },
      { id: "2", source: "s", target: "x", sourceHandle: B },
      { id: "3", source: "other", target: "x", sourceHandle: B },
    ];
    expect(edgesOffKeys(edges, "s", [A]).map((e) => e.id)).toEqual(["2"]);
  });
});
