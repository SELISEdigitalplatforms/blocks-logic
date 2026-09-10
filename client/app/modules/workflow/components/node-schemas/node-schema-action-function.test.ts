import { beforeEach, describe, expect, it, vi } from "vitest";

const { getFunctions } = vi.hoisted(() => ({ getFunctions: vi.fn() }));

vi.mock("@blocks-functions/services/function.service", () => ({
  functionService: { getFunctions },
}));

import { NodeSchemaActionFunction } from "./node-schema-action-function";

const field = (key: string) => NodeSchemaActionFunction.schema.parameters.find((p) => p.key === key)!;

beforeEach(() => {
  vi.clearAllMocks();
});

describe("NodeSchemaActionFunction", () => {
  it("declares the function node's type/category/version", () => {
    expect(NodeSchemaActionFunction.schema.type).toBe("function");
    expect(NodeSchemaActionFunction.schema.category).toBe("action");
    expect(NodeSchemaActionFunction.schema.version).toBe("v1");
  });

  it("defaults to item input mode with no function selected", () => {
    expect(NodeSchemaActionFunction.defaults.parameters).toEqual({
      functionId: "",
      inputMode: "item",
      inputExpression: "",
      waitTimeoutSec: null,
    });
  });

  it("requires a function to be selected", () => {
    expect(field("functionId").required).toBe(true);
  });

  it("only shows the input expression field when inputMode is expression", () => {
    expect(field("inputExpression").dependsOn).toEqual({ key: "inputMode", value: "expression" });
  });

  it("loads only Live functions for the function picker, mapped to id/name options", async () => {
    getFunctions.mockResolvedValue({
      data: [
        { id: "fn_1", name: "Send confirmation" },
        { id: "fn_2", name: "Sync inventory" },
      ],
      totalCount: 2,
    });

    const options = await (field("functionId").options as (data: unknown, config: unknown) => Promise<unknown>)(
      {},
      {},
    );

    expect(getFunctions).toHaveBeenCalledWith(
      expect.objectContaining({ status: "Live" }),
    );
    expect(options).toEqual([
      { value: "fn_1", label: "Send confirmation" },
      { value: "fn_2", label: "Sync inventory" },
    ]);
  });

  it("has an identity transform and a guide, like every other node schema", () => {
    expect(typeof NodeSchemaActionFunction.transform).toBe("function");
    const node = { parameters: { functionId: "fn_1" } };
    expect(NodeSchemaActionFunction.transform!(node as never)).toBe(node);
    expect(NodeSchemaActionFunction.guide).toBeDefined();
  });
});
