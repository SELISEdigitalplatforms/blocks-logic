import { describe, expect, it } from "vitest";
import { getValueByPath, setValueByPath } from "./path-utils";
import {
  cascadeFieldResets,
  isDependencySatisfied,
  resolveDefaultValue,
} from "./dependency-resolver";
// eslint-disable-next-line @typescript-eslint/no-explicit-any
type Field = any;

describe("path-utils", () => {
  it("reads nested values by dotted path", () => {
    const data = { a: { b: { c: 5 } } };
    expect(getValueByPath(data, "a.b.c")).toBe(5);
  });

  it("returns undefined for a missing path", () => {
    expect(getValueByPath({ a: 1 }, "a.b.c")).toBeUndefined();
    expect(getValueByPath({}, "x")).toBeUndefined();
  });

  it("writes nested values immutably", () => {
    const data = { a: { b: 1 } };
    const next = setValueByPath(data, "a.c", 2);
    expect(next).toEqual({ a: { b: 1, c: 2 } });
    expect(data).toEqual({ a: { b: 1 } });
  });

  it("creates intermediate objects when writing a deep path", () => {
    expect(setValueByPath({}, "x.y.z", 9)).toEqual({ x: { y: { z: 9 } } });
  });
});

describe("isDependencySatisfied", () => {
  it("is satisfied when there is no dependency", () => {
    expect(isDependencySatisfied({ key: "a" } as Field, {})).toBe(true);
  });

  it("handles the equals operator", () => {
    const f = { key: "b", dependsOn: { key: "a", value: 1 } } as Field;
    expect(isDependencySatisfied(f, { a: 1 })).toBe(true);
    expect(isDependencySatisfied(f, { a: 2 })).toBe(false);
  });

  it("handles the notEquals operator", () => {
    const f = {
      key: "b",
      dependsOn: { key: "a", value: 1, operator: "notEquals" },
    } as Field;
    expect(isDependencySatisfied(f, { a: 2 })).toBe(true);
    expect(isDependencySatisfied(f, { a: 1 })).toBe(false);
  });

  it("handles the in operator", () => {
    const f = {
      key: "b",
      dependsOn: { key: "a", value: [1, 2], operator: "in" },
    } as Field;
    expect(isDependencySatisfied(f, { a: 2 })).toBe(true);
    expect(isDependencySatisfied(f, { a: 3 })).toBe(false);
  });

  it("returns true for an unknown operator", () => {
    const f = {
      key: "b",
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      dependsOn: { key: "a", value: 1, operator: "weird" as any },
    } as Field;
    expect(isDependencySatisfied(f, { a: 999 })).toBe(true);
  });
});

describe("resolveDefaultValue", () => {
  it("calls a function default with the data", () => {
    expect(
      resolveDefaultValue(
        { key: "a", defaultValue: (d: Record<string, unknown>) => d.x } as Field,
        { x: 7 },
      ),
    ).toBe(7);
  });

  it("returns a static default or null", () => {
    expect(resolveDefaultValue({ key: "a", defaultValue: 3 } as Field, {})).toBe(
      3,
    );
    expect(resolveDefaultValue({ key: "a" } as Field, {})).toBeNull();
  });
});

describe("cascadeFieldResets", () => {
  it("resets dependent fields to their defaults", () => {
    const fields: Field[] = [
      { key: "child", dependsOn: { key: "parent" }, defaultValue: "reset" },
    ];
    const out = cascadeFieldResets(fields, "parent", {
      parent: "x",
      child: "old",
    });
    expect(out.child).toBe("reset");
  });

  it("cascades through chained dependencies", () => {
    const fields: Field[] = [
      { key: "b", dependsOn: { key: "a" }, defaultValue: "B" },
      { key: "c", dependsOn: { key: "b" }, defaultValue: "C" },
    ];
    const out = cascadeFieldResets(fields, "a", { a: 1, b: "x", c: "y" });
    expect(out.b).toBe("B");
    expect(out.c).toBe("C");
  });

  it("skips keys listed in skippedKeys", () => {
    const fields: Field[] = [
      { key: "b", dependsOn: { key: "a" }, defaultValue: "B" },
    ];
    const out = cascadeFieldResets(fields, "a", { a: 1, b: "keep" }, ["b"]);
    expect(out.b).toBe("keep");
  });
});
